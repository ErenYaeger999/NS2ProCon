"""Regression checks for recorded Pro reports and Nintendo advertising formats."""

import unittest

from pc_bridge import decode_report, parse_calibration, unpack_stick
from scan_controller import decode_pro_advertisement


class ProtocolTests(unittest.TestCase):
    def test_recorded_pro_report_offsets(self):
        # Captured from this controller over BLE, characteristic for report 0x09.
        report = bytes.fromhex("0310000000d27788ba888230")
        self.assertEqual(unpack_stick(report[5:8]), (2002, 2183))
        self.assertEqual(unpack_stick(report[8:11]), (2234, 2088))
        calibration = [((2002, 2183), (1500, 1500), (1500, 1500)),
                       ((2234, 2088), (1500, 1500), (1500, 1500))]
        state = decode_report(report, calibration)
        self.assertEqual(state["buttons"], 0)
        self.assertEqual(state["left"], (0, 0))
        self.assertEqual(state["right"], (0, 0))

    def test_label_matched_face_buttons_and_triggers(self):
        calibration = [((2048, 2048), (1500, 1500), (1500, 1500))] * 2
        report = bytearray.fromhex("0030202000000880000880")
        # Report 0x09 bits: B=1, A=2, Y=4, X=8.
        for nintendo_mask, xbox_mask in ((1, 0x2000), (2, 0x1000), (4, 0x8000), (8, 0x4000)):
            report[2] = 0x20 | nintendo_mask
            state = decode_report(report, calibration)
            self.assertEqual(state["buttons"], xbox_mask)
            self.assertEqual((state["lt"], state["rt"]), (255, 255))

    def test_extra_buttons_do_not_press_xbox_buttons(self):
        calibration = [((2048, 2048), (1500, 1500), (1500, 1500))] * 2
        state = decode_report(bytes.fromhex("003000001f000880000880"), calibration)
        self.assertEqual(state["buttons"], 0)
        self.assertEqual(state["extra_buttons"], 31)

    def test_invalid_input_and_calibration(self):
        with self.assertRaises(ValueError):
            decode_report(bytes(10), [])
        self.assertIsNone(parse_calibration(bytes(11)))
        self.assertIsNone(parse_calibration(bytes([255]) * 11))

    def test_recorded_discovery_advertisement(self):
        data = bytes.fromhex("0100037e0569200001000000000000000f00000000000000")
        result = decode_pro_advertisement(data)
        self.assertEqual(result["kind"], "discovery")
        self.assertEqual(result["target_host"], "00:00:00:00:00:00")
        reconnect = bytearray(data)
        reconnect[10:16] = bytes.fromhex("010203040506")
        self.assertEqual(decode_pro_advertisement(reconnect)["kind"], "reconnect")
        reconnect[9] = 0x81
        self.assertEqual(decode_pro_advertisement(reconnect)["kind"], "wake_console")

    def test_ignore_other_products_and_short_advertisements(self):
        self.assertIsNone(decode_pro_advertisement(b""))
        self.assertIsNone(decode_pro_advertisement(bytes.fromhex("0100037e0566200001000000000000000f00000000000000")))


if __name__ == "__main__":
    unittest.main()
