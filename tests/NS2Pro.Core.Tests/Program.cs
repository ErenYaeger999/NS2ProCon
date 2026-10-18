using NS2Pro.Core;
using System.Runtime.InteropServices;

if (args.Contains("--probe-feedback")) { await FeedbackProbe.RunAsync(); return; }
if (OperatingSystem.IsWindows() && args.Contains("--probe-audio")) {
    using var audio = new NS2Pro.App.Services.WindowsAudioDefaults();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Defaults = audio.ReadDefaults().Select(p => new { p.Key.Flow, p.Key.Role, Id = p.Value }), Endpoints = audio.Endpoints() }));
    return;
}
if (args.Contains("--probe-updates")) {
    var updates = new NS2Pro.App.Services.UpdateService();
    for (int attempt = 0; attempt < (args.Contains("--repeat") ? 2 : 1); attempt++)
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await updates.CheckAsync(typeof(NS2Pro.App.Services.UpdateService).Assembly.GetName().Version!)));
    return;
}
if (args.Contains("--probe-viiper")) {
    try { Console.WriteLine(await NS2Pro.App.Services.ViiperGamepad.ProbeAsync()); }
    catch (Exception error) { Console.WriteLine("UNAVAILABLE: " + error.Message); Environment.ExitCode = 2; }
    return;
}

if (OperatingSystem.IsWindows() && SingleInstanceChecks.RunProbe(args) is int probeResult) {
    Environment.ExitCode = probeResult; return;
}

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
Check(state.BatteryPercent == 40 && !state.IsCharging && !state.HasExternalPower, "Recorded battery-only power report");
foreach (byte flags in new byte[] { 1, 2, 3 }) {
    var powerPacket = (byte[])recorded.Clone(); powerPacket[1] |= flags;
    var powered = ControllerProtocol.Decode(powerPacket,lc,rc,ButtonLayout.Labels);
    Check(powered.BatteryPercent == state.BatteryPercent && powered.IsCharging == ((flags & 2) != 0) && powered.HasExternalPower == ((flags & 1) != 0), "Charging and external power flags remain independent of battery level");
    Check(powered.Xbox.Equals(state.Xbox), "Power transitions do not alter game input");
}
var reservedPower = (byte[])recorded.Clone(); reservedPower[1] |= 0xc0;
var reservedState = ControllerProtocol.Decode(reservedPower,lc,rc,ButtonLayout.Labels);
Check(reservedState.BatteryPercent == state.BatteryPercent && !reservedState.IsCharging && !reservedState.HasExternalPower, "Reserved power bits cannot create a false charging indicator");
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
var physicalKeys = new (string Name,int Channel,byte Bit)[] {
    ("B",2,1),("A",2,2),("Y",2,4),("X",2,8),("R",2,16),("ZR",2,32),("+",2,64),("RS",2,128),
    ("↓",3,1),("→",3,2),("←",3,4),("↑",3,8),("L",3,16),("ZL",3,32),("−",3,64),("LS",3,128),
    ("HOME",4,1),("截图",4,2),("GR",4,4),("GL",4,8),("C",4,16)
};
foreach(var key in physicalKeys) {
    var packet=(byte[])recorded.Clone();packet[key.Channel]=key.Bit;
    var decoded=ControllerProtocol.Decode(packet,lc,rc,ButtonLayout.Labels);
    var lit=physicalKeys.Where(k=>ControllerProtocol.IsPressed(k.Name,decoded)).Select(k=>k.Name).ToArray();
    Check(lit.Length==1&&lit[0]==key.Name,"Physical preview isolates "+key.Name);
}
Check(physicalKeys.All(k=>!ControllerProtocol.IsPressed(k.Name,null)),"Disconnected preview has no lit keys");
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
var fresh = new NS2Pro.App.Services.AppSettings();
Check(fresh.LastControllerAddress == "" && !fresh.HasCompletedFirstConnection, "Fresh install discovers the user's controller and opens onboarding");
Check(fresh.ShouldShowConnectionGuide, "Guide appears for a new install");
fresh.HasSeenConnectionGuide = true;
var reopened = NS2Pro.App.Services.AppSettings.FromJson(System.Text.Json.JsonSerializer.Serialize(fresh));
Check(!reopened.ShouldShowConnectionGuide && !reopened.HasCompletedFirstConnection, "A returning user opens overview even if they have not connected yet");
var upgraded = NS2Pro.App.Services.AppSettings.FromJson("{\"HasCompletedFirstConnection\":false}");
Check(upgraded.HasSeenConnectionGuide && !upgraded.ShouldShowConnectionGuide, "Existing settings migrate to overview without repeating onboarding");
var connectedUser = new NS2Pro.App.Services.AppSettings { HasCompletedFirstConnection = true };
connectedUser.Normalize();
Check(connectedUser.HasSeenConnectionGuide && !connectedUser.ShouldShowConnectionGuide, "A previously connected user does not repeat onboarding");
Check(!System.Text.Json.JsonSerializer.Serialize(fresh).Contains("ShouldShowConnectionGuide"), "Derived launch routing is not written as a user setting");
Check(!new NS2Pro.App.Services.AppSettings().CloseToTray, "Close button exits by default");
Check(!NS2Pro.App.Services.AppSettings.FromJson("{\"MinimizeToTray\":true}").CloseToTray, "Legacy implicit tray setting does not keep the app running after close");
Check(NS2Pro.App.Services.AppSettings.FromJson("{\"CloseToTray\":true}").CloseToTray, "Explicit background-running preference survives restart");
foreach (var phase in Enum.GetValues<NS2Pro.App.Services.ConnectionPhase>()) {
    var connection = new NS2Pro.App.Services.ConnectionInfo(phase, "");
    Check(!(connection.CanStart && connection.CanStop), "A connection state cannot offer start and stop simultaneously: " + phase);
    Check(connection.IsActive == !connection.CanStart, "Guide and main action agree on active states: " + phase);
}
var waiting = new NS2Pro.App.Services.ConnectionInfo(NS2Pro.App.Services.ConnectionPhase.WaitingForInput, "");
Check(waiting.HasLink && waiting.CanStop && waiting.ActionText == "断开 PC 连接", "A link waiting for input can be disconnected without advertising ready input");
var failure = new NS2Pro.App.Services.ConnectionInfo(NS2Pro.App.Services.ConnectionPhase.Error, "");
Check(failure.CanStart && failure.ActionText == "重试连接", "Stopped failure has an explicit retry action");
var stopping = new NS2Pro.App.Services.ConnectionInfo(NS2Pro.App.Services.ConnectionPhase.Stopping, "");
Check(!stopping.CanStart && !stopping.CanStop, "Disconnecting cannot be interrupted by another connection action");
var notices = new NS2Pro.App.Services.AppSettings();
Check(notices.ShouldShowConnectionNotice && notices.ConnectionNoticeDismissCount == 0, "New and legacy settings start with the combined connection notice enabled");
for (int count = 1; count <= 3; count++) {
    notices.DismissConnectionNotice();
    notices = NS2Pro.App.Services.AppSettings.FromJson(System.Text.Json.JsonSerializer.Serialize(notices));
    Check(notices.ConnectionNoticeDismissCount == count && notices.ShouldShowConnectionNotice == (count < 3), "Explicit notice dismissals survive restart and stop after the third close");
}
notices.DismissConnectionNotice();
Check(notices.ConnectionNoticeDismissCount == 3, "Permanently dismissed notice cannot increment beyond three");
notices.ConnectionNoticeDismissCount = int.MaxValue; notices.Normalize();
Check(notices.ConnectionNoticeDismissCount == 3 && !notices.ShouldShowConnectionNotice, "Out-of-range notice counts stay permanently hidden");
notices.ConnectionNoticeDismissCount = -4; notices.Normalize();
Check(notices.ConnectionNoticeDismissCount == 0 && notices.ShouldShowConnectionNotice, "Invalid negative notice count recovers safely");
var invalid = System.Text.Json.JsonSerializer.Deserialize<NS2Pro.App.Services.AppSettings>("{\"LastControllerAddress\":null,\"Layout\":99,\"Deadzone\":-2}")!;
invalid.Normalize();
Check(invalid.LastControllerAddress == "" && invalid.Layout == ButtonLayout.Labels && invalid.Deadzone == 0, "Old or malformed settings cannot break discovery or mapping");
invalid.LastControllerAddress = " not-an-address "; invalid.Deadzone = double.NaN; invalid.Normalize();
Check(invalid.LastControllerAddress == "" && invalid.Deadzone == .06, "Invalid address and nonfinite deadzone recover safely");
invalid.LastControllerAddress = " aa:bb:cc:dd:ee:ff "; invalid.Normalize();
Check(invalid.LastControllerAddress == "AA:BB:CC:DD:EE:FF", "Remembered controller address is normalized");
Check(SystemRequirements.GetIssue(new Version(10,0,19041),Architecture.X64) is null, "Minimum Windows 10 build accepted");
Check(SystemRequirements.GetIssue(new Version(10,0,19040),Architecture.X64) is not null, "Earlier Windows build rejected");
Check(SystemRequirements.GetIssue(new Version(10,0,26100),Architecture.X64) is null, "Windows 11 accepted");
Check(SystemRequirements.GetIssue(new Version(10,0,26100),Architecture.Arm64) is not null, "Untested ARM64 is not advertised as supported");
Check(SystemRequirements.GetIssue(new Version(10,0,26100),Architecture.X86) is not null, "32-bit Windows rejected");
if (OperatingSystem.IsWindows()) passed += SingleInstanceChecks.Run();
passed += await HapticChecks.RunAsync();
passed += await NativeHapticChecks.RunAsync();
passed += await UpdateChecks.RunAsync();
passed += await EnhancementChecks.RunAsync();
passed += AudioDefaultChecks.Run();
passed += await WorkflowChecks.RunAsync();
passed += await SecureChannelChecks.RunAsync();
Console.WriteLine($"PASS: {passed} protocol, settings, haptics, updates and single-instance checks.");
