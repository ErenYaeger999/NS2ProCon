"""Try a temporary BLE connection without writing controller pairing data.

Only accepts a Switch 2 Pro in SYNC/discovery mode. Subscribes to the two
documented input characteristics, sends no proprietary commands, then disconnects.
"""

import argparse
import asyncio
import json
import time
from datetime import datetime, timezone
from pathlib import Path

from bleak import BleakClient, BleakScanner

from scan_controller import decode_pro_advertisement

INPUT_UUIDS = {
    "ab7de9be-89fe-49ad-828f-118f09df7fd2": "common_input_05",
    "7492866c-ec3e-4619-8258-32755ffcc0f8": "pro_input_09",
}


async def run(args):
    result = {"started": datetime.now(timezone.utc).isoformat(), "advertisements": [],
              "status": "scanning", "pairing_commands_sent": 0}
    args.output.parent.mkdir(parents=True, exist_ok=True)

    def save():
        args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")

    ready = asyncio.get_running_loop().create_future()
    seen = set()

    def discovered(device, advertisement):
        payload = advertisement.manufacturer_data.get(0x0553)
        decoded = decode_pro_advertisement(payload) if payload is not None else None
        if decoded is None or (args.address and device.address.lower() != args.address.lower()):
            return
        key = (device.address, bytes(payload))
        if key not in seen:
            seen.add(key)
            entry = {"address": device.address, **decoded}
            result["advertisements"].append(entry)
            print(json.dumps(entry), flush=True)
            save()
        if decoded["kind"] == "discovery" and not ready.done():
            ready.set_result(device)

    save()
    print(f"Waiting up to {args.wait:g}s for a Switch 2 Pro in SYNC mode.", flush=True)
    try:
        async with BleakScanner(detection_callback=discovered, scanning_mode="passive"):
            device = await asyncio.wait_for(ready, args.wait)
        result["address"] = device.address
        result["status"] = "connecting"
        save()
        async with BleakClient(device, pair=False, timeout=20) as client:
            result["status"] = "connected"
            result["services"] = [
                {"uuid": service.uuid, "characteristics": [
                    {"uuid": ch.uuid, "properties": ch.properties}
                    for ch in service.characteristics]}
                for service in client.services
            ]
            reports = {}
            samples = {}
            result["reports"] = reports
            result["samples"] = samples
            subscribed = []

            def received(characteristic, data):
                label = INPUT_UUIDS[characteristic.uuid]
                now = time.monotonic()
                stats = reports.setdefault(label, {"count": 0, "first": now, "last": now})
                stats["count"] += 1
                stats["last"] = now
                entries = samples.setdefault(label, [])
                if len(entries) < 12 and data.hex() not in entries:
                    entries.append(data.hex())
                if stats["count"] == 1:
                    print(f"Received {label}, {len(data)} bytes.", flush=True)

            for uuid in INPUT_UUIDS:
                characteristic = client.services.get_characteristic(uuid)
                if characteristic is not None and "notify" in characteristic.properties:
                    await client.start_notify(characteristic, received)
                    subscribed.append(uuid)
            result["subscribed"] = subscribed
            save()
            print(f"Connected without pairing writes. Press A/B and move sticks for {args.seconds:g}s.", flush=True)
            await asyncio.sleep(args.seconds)
            for stats in reports.values():
                duration = stats["last"] - stats["first"]
                stats["observed_hz"] = (stats["count"] - 1) / duration if duration > 0 else None
            result["status"] = "input_received" if reports else "connected_no_input"
        print(f"Probe finished: {result['status']}; disconnected.", flush=True)
    except TimeoutError:
        result["error"] = "Timed out during " + result["status"]
        result["status"] = "timeout"
        print(result["error"], flush=True)
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["status"] = "error"
        print(result["error"], flush=True)
    finally:
        save()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wait", type=float, default=180)
    parser.add_argument("--seconds", type=float, default=30)
    parser.add_argument("--address", help="Restrict the test to this controller Bluetooth address")
    parser.add_argument("--output", type=Path, default=Path("research/connection-probe.json"))
    args = parser.parse_args()
    if not 0 < args.wait <= 600 or not 0 < args.seconds <= 120:
        parser.error("wait must be 0..600 seconds and seconds 0..120")
    asyncio.run(run(args))
