using NS2Pro.Core;
using System.Runtime.InteropServices;

int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
var standard = Convert.FromHexString("0100037E0569200001000000000000000F00000000000000");
Check(ControllerProtocol.ParseAdvertisement(standard)?.Kind == AdvertisementKind.Discovery, "SYNC discovery");
var reconnect = (byte[])standard.Clone(); reconnect[10] = 1;
Check(ControllerProtocol.ParseAdvertisement(reconnect)?.Kind == AdvertisementKind.Reconnect, "Do not capture console reconnection");
reconnect[9] = 0x81;
Check(ControllerProtocol.ParseAdvertisement(reconnect)?.Kind == AdvertisementKind.WakeConsole, "Do not capture console wake");
standard[5] = 0x66;
Check(ControllerProtocol.ParseAdvertisement(standard) is null, "Ignore other Nintendo products");
Check(ControllerProtocol.ParseAdvertisement([]) is null, "Reject truncated advertisement");

var recorded = Convert.FromHexString("0310000000D27788BA888230");
Check(ControllerProtocol.UnpackStick(recorded.AsSpan(5)) == new Stick(2002,2183), "Recorded left stick offsets");
Check(ControllerProtocol.UnpackStick(recorded.AsSpan(8)) == new Stick(2234,2088), "Recorded right stick offsets");
var lc = new StickCalibration(new(2002,2183),new(1500,1500),new(1500,1500));
var rc = new StickCalibration(new(2234,2088),new(1500,1500),new(1500,1500));
var state = ControllerProtocol.Decode(recorded,lc,rc,ButtonLayout.Labels);
Check(state.Left == new Stick(0,0) && state.Right == new Stick(0,0), "Recorded neutral input");
Check(state.Xbox.Buttons == 0, "No phantom button presses");
foreach (var (bit,label,position) in new (byte,ushort,ushort)[] {
    (1,0x2000,0x1000),(2,0x1000,0x2000),(4,0x8000,0x4000),(8,0x4000,0x8000) }) {
    var packet = (byte[])recorded.Clone(); packet[2] = bit;
    Check(ControllerProtocol.Decode(packet,lc,rc,ButtonLayout.Labels).Xbox.Buttons == label, "Letter preset");
    Check(ControllerProtocol.Decode(packet,lc,rc,ButtonLayout.Positions).Xbox.Buttons == position, "Physical position preset");
}
var triggerPacket = (byte[])recorded.Clone(); triggerPacket[2] = triggerPacket[3] = 0x20;
var triggers = ControllerProtocol.Decode(triggerPacket,lc,rc,ButtonLayout.Labels).Xbox;
Check(triggers.LeftTrigger == 255 && triggers.RightTrigger == 255, "Digital triggers");
var extraPacket = (byte[])recorded.Clone(); extraPacket[4] = 31;
var extras = ControllerProtocol.Decode(extraPacket,lc,rc,ButtonLayout.Labels);
Check(extras.ExtraButtons == 31 && extras.Xbox.Buttons == 0, "Extra inputs observed without OS shortcuts");
Check(ControllerProtocol.Normalize(new(2020,2183),lc,.06) == new Stick(0,0), "Deadzone suppresses small drift");
Check(ControllerProtocol.Normalize(new(2020,2183),lc,0).X > 0, "Tester retains input before deadzone");
Check(ControllerProtocol.ParseCalibration(new byte[11]) is null, "Reject missing calibration");
Check(ControllerProtocol.ParseCalibration(Enumerable.Repeat((byte)255,11).ToArray()) is null, "Reject blank flash calibration");
var read = ControllerProtocol.CalibrationReadRequest(0x1fc042);
Check(Convert.ToHexString(read) == "02910104000800000B7E000042C01F00", "Calibration command is memory READ 02/04");
var response = new byte[27]; response[0]=2; response[1]=1; response[3]=4; response[8]=11;
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(12),0x1fc042);
Check(ControllerProtocol.ParseCalibrationResponse(response,0x1fc042).Length == 11,"Matching response accepted");
bool wrong = false;
try { ControllerProtocol.ParseCalibrationResponse(response,0x130a8); } catch (InvalidDataException) { wrong = true; }
Check(wrong,"Mismatched read response rejected");
Check(Marshal.SizeOf<XboxReport>() == 12,"XInput native ABI structure size");
Console.WriteLine($"PASS: {passed} protocol and mapping checks.");
