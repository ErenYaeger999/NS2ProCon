"""Switch 2 Pro BLE -> Xbox 360, preserving the controller's console pairing.

Protocol: https://github.com/ndeadly/switch2_controller_research
This program accepts only undirected SYNC advertisements from the chosen pad.
Its only proprietary command is memory READ (02/04) for stick calibration.
It never sends pairing, flash-write, firmware-update, or console-wake commands.
Close with Ctrl+C before reconnecting the controller to the console.
"""

import argparse
import asyncio
import json
import math
import sys
import time
from pathlib import Path

from bleak import BleakClient, BleakScanner

from scan_controller import decode_pro_advertisement

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "research" / "vgamepad-0.1.0"))

PRO_INPUT = "7492866c-ec3e-4619-8258-32755ffcc0f8"
COMMAND = "649d4ac9-8eb7-4e6c-af44-1ea54fe5f005"
RESPONSE = "c765a961-d9d8-4d36-a20a-5315b111836a"


def unpack_stick(data):
    return data[0] | ((data[1] & 15) << 8), (data[1] >> 4) | (data[2] << 4)


def parse_calibration(data):
    if len(data) < 9:
        return None
    center, maximum, minimum = (unpack_stick(data[x:x + 3]) for x in (0, 3, 6))
    if not all(256 <= n <= 3840 for n in center):
        return None
    if not all(128 <= n <= 4095 for n in maximum + minimum):
        return None
    return center, maximum, minimum


def normalize_stick(raw, calibration, deadzone=0.06):
    center, maximum, minimum = calibration
    values = []
    for value, midpoint, high, low in zip(raw, center, maximum, minimum):
        offset = value - midpoint
        values.append(max(-1.0, min(1.0, offset / (high if offset >= 0 else low))))
    length = math.hypot(*values)
    if length <= deadzone:
        return 0.0, 0.0
    gain = min(1.0, (length - deadzone) / (1.0 - deadzone)) / length
    return tuple(v * gain for v in values)


def decode_report(data, calibration):
    if len(data) < 11:
        raise ValueError("Truncated Pro input report")
    right, left, extra = data[2:5]
    # Match button labels: Nintendo A/B/X/Y -> Xbox A/B/X/Y.
    mapping = (
        (right, 0x01, 0x2000), (right, 0x02, 0x1000),
        (right, 0x04, 0x8000), (right, 0x08, 0x4000),
        (right, 0x10, 0x0200), (right, 0x40, 0x0010),
        (right, 0x80, 0x0080), (left, 0x01, 0x0002),
        (left, 0x02, 0x0008), (left, 0x04, 0x0004),
        (left, 0x08, 0x0001), (left, 0x10, 0x0100),
        (left, 0x40, 0x0020), (left, 0x80, 0x0040),
    )
    buttons = 0
    for source, bit, output in mapping:
        if source & bit:
            buttons |= output
    return {
        "buttons": buttons,
        "lt": 255 if left & 0x20 else 0,
        "rt": 255 if right & 0x20 else 0,
        "left": normalize_stick(unpack_stick(data[5:8]), calibration[0]),
        "right": normalize_stick(unpack_stick(data[8:11]), calibration[1]),
        "extra_buttons": extra,
    }


async def read_calibration(client):
    pending = None

    def response(_characteristic, data):
        if (pending is not None and not pending.done() and len(data) >= 8
                and data[0] == 0x02 and data[1] == 0x01 and data[3] == 0x04):
            pending.set_result(bytes(data[8:]))

    async def read_memory(address):
        nonlocal pending
        length = 11
        payload = bytes([length, 0x7e, 0, 0]) + address.to_bytes(4, "little")
        packet = bytes([2, 0x91, 1, 4, 0, len(payload), 0, 0]) + payload
        pending = asyncio.get_running_loop().create_future()
        await client.write_gatt_char(COMMAND, packet, response=False)
        data = await asyncio.wait_for(pending, 3)
        if len(data) < 8 + length or data[0] != length or int.from_bytes(data[4:8], "little") != address:
            raise ValueError("Unexpected calibration read response")
        return data[8:8 + length]

    await client.start_notify(RESPONSE, response)
    try:
        calibrations = []
        for user, factory in ((0x1fc042, 0x130a8), (0x1fc062, 0x130e8)):
            calibration = parse_calibration(await read_memory(user))
            if calibration is None:
                calibration = parse_calibration(await read_memory(factory))
            if calibration is None:
                raise ValueError("No valid joystick calibration found")
            calibrations.append(calibration)
        return calibrations
    finally:
        await client.stop_notify(RESPONSE)


async def connect_once(args, device):
    import vgamepad
    disconnected = asyncio.Event()
    stats = {"address": device.address, "reports": 0, "pairing_writes": 0,
             "status": "connecting", "buttons_seen": 0}
    output = ROOT / "research" / "bridge-last-session.json"
    native_device = request = gamepad = None
    notify_started = False
    accepting_input = True
    async with BleakClient(device, pair=False, timeout=20,
                           disconnected_callback=lambda _: disconnected.set()) as client:
        try:
            try:
                from winrt.windows.devices.bluetooth import (
                    BluetoothLEDevice, BluetoothLEPreferredConnectionParameters,
                )
                native_device = await BluetoothLEDevice.from_bluetooth_address_async(
                    int(device.address.replace(":", ""), 16))
                if native_device:
                    request = native_device.request_preferred_connection_parameters(
                        BluetoothLEPreferredConnectionParameters.throughput_optimized)
                    stats["preferred_connection_request"] = request.status.name
            except Exception as error:
                stats["connection_preference_error"] = str(error)

            calibration = await read_calibration(client)
            stats["calibration"] = calibration
            gamepad = vgamepad.VX360Gamepad()
            start = time.monotonic()
            last_input = start
            last_print = start

            def on_input(_characteristic, data):
                nonlocal last_input, last_print
                if not accepting_input or len(data) < 11:
                    return
                state = decode_report(data, calibration)
                now = time.monotonic()
                last_input = now
                report = gamepad.report
                report.wButtons = state["buttons"]
                report.bLeftTrigger = state["lt"]
                report.bRightTrigger = state["rt"]
                report.sThumbLX, report.sThumbLY = [round(n * 32767) for n in state["left"]]
                report.sThumbRX, report.sThumbRY = [round(n * 32767) for n in state["right"]]
                gamepad.update()
                stats["reports"] += 1
                stats["buttons_seen"] |= state["buttons"]
                stats["last_input"] = state
                stats["observed_hz"] = stats["reports"] / max(now - start, 0.001)
                if now - last_print >= 5:
                    print(f"Connected | {stats['observed_hz']:.1f} reports/s | Xbox buttons {state['buttons']:04X}", flush=True)
                    output.write_text(json.dumps(stats, indent=2), encoding="utf-8")
                    last_print = now

            await client.start_notify(PRO_INPUT, on_input)
            notify_started = True
            stats["status"] = "connected"
            print("CONNECTED: Xbox 360 output active. Button labels match: A->A, B->B, X->X, Y->Y.", flush=True)
            print("Ctrl+C disconnects PC. Then use the controller's usual console reconnect action.", flush=True)
            deadline = start + args.duration if args.duration else float("inf")
            while not disconnected.is_set() and time.monotonic() < deadline:
                try:
                    await asyncio.wait_for(disconnected.wait(), 0.25)
                except TimeoutError:
                    pass
                # Release held controls if the radio silently stops delivering inputs.
                if time.monotonic() - last_input > 1:
                    gamepad.reset()
                    gamepad.update()
            stats["status"] = "finished"
        finally:
            accepting_input = False
            if stats["status"] == "connected":
                stats["status"] = "stopped"
            if notify_started and client.is_connected:
                try:
                    await client.stop_notify(PRO_INPUT)
                except Exception:
                    pass
            if gamepad is not None:
                gamepad.reset()
                gamepad.update()
                del gamepad
            if request is not None:
                request.close()
            if native_device is not None:
                native_device.close()
            output.write_text(json.dumps(stats, indent=2), encoding="utf-8")


async def main(args):
    while True:
        ready = asyncio.get_running_loop().create_future()

        def found(device, advertisement):
            if device.address.lower() != args.address.lower():
                return
            payload = advertisement.manufacturer_data.get(0x0553)
            decoded = decode_pro_advertisement(payload) if payload else None
            if decoded and decoded["kind"] == "discovery" and not ready.done():
                ready.set_result(device)

        print("Hold the controller's SYNC button to connect to PC. Do not use HOME to start PC mode.", flush=True)
        async with BleakScanner(detection_callback=found, scanning_mode="passive"):
            device = await asyncio.wait_for(ready, args.wait) if args.wait else await ready
        try:
            await connect_once(args, device)
        except Exception as error:
            print(f"Connection error: {type(error).__name__}: {error}", flush=True)
            if args.duration:
                raise
            await asyncio.sleep(2)
        if args.duration:
            break
        print("Disconnected. Waiting for a new SYNC action.", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--address", required=True)
    parser.add_argument("--duration", type=float, default=0, help="Stop after N connected seconds; 0 keeps running")
    parser.add_argument("--wait", type=float, default=0, help="Discovery timeout; 0 keeps waiting")
    args = parser.parse_args()
    if args.duration < 0 or args.wait < 0:
        parser.error("duration and wait must be nonnegative")
    try:
        asyncio.run(main(args))
    except KeyboardInterrupt:
        print("PC connection closed.")
