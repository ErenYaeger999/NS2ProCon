"""Inspect the default Windows Bluetooth radio; optionally enable it."""

import argparse
import asyncio
import json

from winrt.windows.devices.bluetooth import BluetoothAdapter
from winrt.windows.devices.radios import Radio, RadioAccessStatus, RadioState


async def main(enable):
    adapter = await BluetoothAdapter.get_default_async()
    if adapter is None:
        raise RuntimeError("No default Bluetooth adapter found")
    radio = await adapter.get_radio_async()
    result = {
        "radio": radio.name,
        "ble_supported": adapter.is_low_energy_supported,
        "initial_state": radio.state.name,
    }
    if enable and radio.state != RadioState.ON:
        access = await Radio.request_access_async()
        result["access"] = access.name
        if access != RadioAccessStatus.ALLOWED:
            raise RuntimeError(f"Bluetooth radio access: {access.name}")
        change = await radio.set_state_async(RadioState.ON)
        result["set_state_result"] = change.name
        await asyncio.sleep(1)
    result["state"] = radio.state.name
    print(json.dumps(result))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--enable", action="store_true")
    args = parser.parse_args()
    asyncio.run(main(args.enable))
