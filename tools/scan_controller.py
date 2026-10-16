"""Passively observe Switch 2 Pro advertisements; never connect or pair.

Protocol reference: ndeadly/switch2_controller_research/bluetooth_interface.md
Bleak's manufacturer_data excludes the two-byte company identifier, hence the
offsets below are two bytes smaller than those in the reference's full record.
"""

import argparse
import asyncio
import json
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from bleak import BleakScanner


def decode_pro_advertisement(payload: bytes):
    if len(payload) < 17 or payload[:3] != b"\x01\x00\x03":
        return None
    if int.from_bytes(payload[3:5], "little") != 0x057E:
        return None
    if int.from_bytes(payload[5:7], "little") != 0x2069:
        return None
    flag = payload[9]
    target = payload[10:16]
    if flag == 0x81:
        kind = "wake_console"
    elif flag == 0 and target == bytes(6):
        kind = "discovery"
    elif flag == 0:
        kind = "reconnect"
    else:
        kind = "unknown"
    return {
        "kind": kind,
        "wake_flag": f"0x{flag:02x}",
        "target_host": ":".join(f"{b:02X}" for b in reversed(target)),
        "manufacturer_payload": payload.hex(),
    }


async def scan(seconds: float, output: Path):
    output.parent.mkdir(parents=True, exist_ok=True)
    counts = Counter()
    last_seen = {}
    print(f"Passive BLE scan for {seconds:g}s; output: {output}", flush=True)
    with output.open("w", encoding="utf-8") as stream:
        def detected(device, advertisement):
            payload = advertisement.manufacturer_data.get(0x0553)
            if payload is None:
                return
            decoded = decode_pro_advertisement(payload)
            if decoded is None:
                return
            counts[decoded["kind"]] += 1
            record = {
                "time": datetime.now(timezone.utc).isoformat(),
                "address": device.address,
                "rssi": advertisement.rssi,
                **decoded,
            }
            stream.write(json.dumps(record) + "\n")
            stream.flush()
            identity = (decoded["kind"], decoded["target_host"])
            if last_seen.get(device.address) != identity:
                print(json.dumps(record), flush=True)
                last_seen[device.address] = identity

        async with BleakScanner(detection_callback=detected, scanning_mode="passive"):
            await asyncio.sleep(seconds)
    summary = {
        "scan_seconds": seconds,
        "controllers_seen": len(last_seen),
        "advertisement_events": dict(counts),
        "limitation": "Windows-delivered BLE advertisements only; missing a wake advertisement does not prove none was transmitted.",
    }
    output.with_suffix(".summary.json").write_text(
        json.dumps(summary, indent=2), encoding="utf-8"
    )
    print(json.dumps(summary), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seconds", type=float, default=30)
    parser.add_argument("--output", type=Path, default=Path("research/ble-scan.jsonl"))
    args = parser.parse_args()
    if not 0 < args.seconds <= 600:
        parser.error("--seconds must be between 0 and 600")
    try:
        asyncio.run(scan(args.seconds, args.output))
    except KeyboardInterrupt:
        print("Scan stopped.")
