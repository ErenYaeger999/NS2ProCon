using System.Buffers.Binary;
using System.Diagnostics;
using NS2Pro.App.Services;
using NS2Pro.Core;

internal static class MotionChecks
{
    public static int Run()
    {
        int passed = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Motion: " + message); passed++; }
        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-8;
        static byte[] Sample(uint sequence, short gx = 20, short gy = -10, short gz = 5)
        {
            var packet = new byte[63];
            BinaryPrimitives.WriteUInt32LittleEndian(packet, sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(42), unchecked(sequence * 15000));
            BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(52), 4096);
            BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(54), gx);
            BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(56), gy);
            BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(58), gz);
            return packet;
        }
        Check(Enumerable.Range(0, 60).All(n => MotionProtocol.Decode(new byte[n]) is null), "all incomplete FD2 packets are rejected");
        var raw = Sample(37);
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(46), 1269);
        var decoded = MotionProtocol.Decode(raw)!;
        Check(decoded.Sequence == 37 && decoded.DeviceTimestamp == 555000 && Near(decoded.Temperature, 35), "FD2 counter, independent sensor timestamp and temperature offsets");
        Check(Near(decoded.Gyroscope.X, 1.4) && Near(decoded.Gyroscope.Y, .35) && Near(decoded.Gyroscope.Z, .7), "Pro gain and all three signed Y-up axis directions");
        Check(decoded.Acceleration == new MotionVector(0, 1, 0), "physical gravity becomes canonical 1g without gyro units");
        Check(MotionProtocol.FeatureRequest(2).SequenceEqual(new byte[] { 12, 145, 1, 2, 0, 4, 0, 0, 39, 0, 0, 0 }), "mask preserves button, stick and rumble feature positions");
        Check(MotionProtocol.FeatureRequest(4)[8] == 0x27 && MotionProtocol.FeatureRequest(5)[8] == 4, "common input enables control/IMU features; stopping disables only IMU");
        bool rejected = false; try { MotionProtocol.FeatureRequest(7); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "unknown/pairing commands cannot be generated");
        Check(MotionProtocol.SelectReportRequest(5).SequenceEqual(new byte[] { 3, 145, 1, 10, 0, 4, 0, 0, 5, 0, 0, 0 }) &&
            MotionProtocol.SelectReportRequest(9)[8] == 9 && MotionProtocol.StartPublicReportsRequest()[3] == 13,
            "report selection and volatile public startup are distinct from pair/clear/store commands");
        rejected = false; try { MotionProtocol.SelectReportRequest(7); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "public report selector cannot generate arbitrary command/subcommand combinations");
        var center = new StickCalibration(new(2048, 2048), new(2047, 2047), new(2048, 2048));
        var common = new byte[63];
        new byte[] { 0, 8, 128, 0, 8, 128 }.CopyTo(common, 10);
        var neutralControls = ControllerProtocol.DecodeCommon(common, center, center, ButtonLayout.Labels, .06, 0x17);
        Check(neutralControls.Left == new Stick(0, 0) && neutralControls.Right == new Stick(0, 0) &&
            neutralControls.BatteryPercent == 50 && neutralControls.IsCharging && neutralControls.HasExternalPower,
            "FD2 uses calibrated sticks and original native power bits, not a guessed voltage percentage");
        foreach (var (bit, name) in new (int, string)[] { (0,"Y"),(1,"X"),(2,"B"),(3,"A"),(6,"R"),(7,"ZR"),
            (8,"−"),(9,"+"),(10,"RS"),(11,"LS"),(12,"HOME"),(13,"截图"),(14,"C"),
            (16,"↓"),(17,"↑"),(18,"→"),(19,"←"),(22,"L"),(23,"ZL"),(24,"GR"),(25,"GL") }) {
            BinaryPrimitives.WriteUInt32LittleEndian(common.AsSpan(4), 1u << bit);
            var controls = ControllerProtocol.DecodeCommon(common, center, center, ButtonLayout.Labels, .06, 0x14);
            Check(ControllerProtocol.IsPressed(name, controls) &&
                System.Numerics.BitOperations.PopCount((uint)controls.RightButtons | ((uint)controls.LeftButtons << 8) | ((uint)controls.ExtraButtons << 16)) == 1,
                "FD2 control isolates the correct existing preview and mapping key: " + name);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(common.AsSpan(4), 8);
        Check(ControllerProtocol.DecodeCommon(common, center, center, ButtonLayout.Labels, 0, 0x14).Xbox.Buttons == 0x1000 &&
            ControllerProtocol.DecodeCommon(common, center, center, ButtonLayout.Positions, 0, 0x14).Xbox.Buttons == 0x2000,
            "FD2 honors both existing A-label and physical-position presets");
        new byte[] { 0, 4, 192, 255, 255, 255 }.CopyTo(common, 10);
        var axes = ControllerProtocol.DecodeCommon(common, center, center, ButtonLayout.Labels, 0, 0x14);
        Check(Near(axes.Left.X, -.5) && axes.Left.Y is > .5 and < .501 && Near(axes.Right.X, Math.Sqrt(.5)) && Near(axes.Right.Y, Math.Sqrt(.5)),
            "common packed sticks keep range, sign and existing endpoint calibration");
        rejected = false; try { ControllerProtocol.DecodeCommon(new byte[15], center, center, ButtonLayout.Labels, 0, 0); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "a truncated common control report cannot replace healthy native input");
        long now = Stopwatch.Frequency * 10;
        var motion = new MotionState(new(10, -20, 30), new(.25, 1, -.5), now);
        var wire = DualSenseWire.Input(new XboxReport { Buttons = 0x1000, RightTrigger = 255 }, motion);
        Check(BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(21)) == 160 &&
            BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(23)) == -320 && BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(25)) == 480,
            "VIIPER V5 uses calibrated 16 units/degree/s at 21,23,25");
        Check(BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(27)) == 2048 &&
            BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(29)) == 8192 && BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(31)) == -4096,
            "V5 acceleration uses 8192 counts/g and preserves all axes");
        Check(wire.Length == 33 && BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(4)) == 0x820 && wire[10] == 255 && wire.AsSpan(11, 10).ToArray().All(x => x == 0),
            "six-axis input does not change buttons, triggers, touch fields or negotiated wire size");
        var neutral = DualSenseWire.Input(default);
        Check(neutral.AsSpan(21, 6).ToArray().All(x => x == 0) && BinaryPrimitives.ReadInt16LittleEndian(neutral.AsSpan(29)) == 8192,
            "neutral output has no rotation and a resting gravity vector, not freefall");
        var extremes = DualSenseWire.Input(default, new(new(double.MaxValue, double.NaN, -double.MaxValue), new(8, -8, double.PositiveInfinity), now));
        Check(BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(21)) == short.MaxValue &&
            BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(23)) == 0 && BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(25)) == short.MinValue,
            "wire conversion saturates huge finite gyro and rejects non-finite values");
        Check(BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(27)) == short.MaxValue &&
            BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(29)) == short.MinValue && BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(31)) == 0,
            "acceleration extremes cannot overflow the signed 16-bit wire");
        Check(MotionProtocol.IsFresh(motion, now) && !MotionProtocol.IsFresh(motion, now - 1) &&
            !MotionProtocol.IsFresh(motion, now + Stopwatch.Frequency) && !MotionProtocol.IsFresh(null, now), "future, missing and expired samples are not current input");

        var session = new MotionSession(timestamp: () => now);
        Check(!session.BeginCalibration() && session.Output is null, "disconnected/missing sensors cannot start a calibration");
        Check(!session.Submit(new byte[63], now), "masked zero-filled IMU blocks are not reported as valid motion");
        Check(session.Submit(Sample(uint.MaxValue), now), "first FD2 sample is accepted");
        now += Stopwatch.Frequency / 66;
        Check(session.Submit(Sample(0), now), "physical sequence wraps without a false reset");
        now += Stopwatch.Frequency / 66;
        Check(!session.Submit(Sample(0), now) && !session.Submit(Sample(uint.MaxValue), now), "duplicate and reordered packets do not advance physical rate or motion");
        Check(session.Status.Samples == 2 && session.Status.Hertz is > 65 and < 67, "sampling rate counts unique physical samples, not virtual timer ticks");
        now += Stopwatch.Frequency;
        Check(session.Output is null && !session.Status.Receiving, "data expiry stops motion without needing another notification");
        Check(session.Submit(Sample(0), now), "after a real gap a controller counter reset starts a new epoch");
        Check(session.BeginCalibration(), "fresh sensor stream can start explicit static calibration");
        for (uint i = 1; i <= 145; i++) { now += Stopwatch.Frequency * 15 / 1000; session.Submit(Sample(i), now); }
        var calibration = session.TakeCalibration();
        Check(calibration is { } bias && Near(bias.X, 1.4) && Near(bias.Y, .35) && Near(bias.Z, .7) && !session.Status.Calibrating,
            "two seconds of stationary data produce a per-controller gyro bias");
        Check(session.Output is { } corrected && corrected.Gyroscope.Length < 1e-8 && corrected.Acceleration == new MotionVector(0, 1, 0),
            "calibration removes gyro bias without removing gravity or changing acceleration gain");
        Check(session.TakeCalibration() is null, "completed calibration is persisted only once");
        Check(session.BeginCalibration(), "repeat calibration can replace a prior result");
        for (uint i = 146; i <= 160; i++) { now += Stopwatch.Frequency * 15 / 1000; session.Submit(Sample(i, i % 2 == 0 ? (short)1000 : (short)-1000), now); }
        Check(session.Status.Calibrating && session.Status.Progress == 0 && session.TakeCalibration() is null,
            "motion during calibration resets the stable window instead of becoming a new bias");
        now += Stopwatch.Frequency;
        Check(!session.Status.Calibrating && session.Status.Calibrated && session.TakeCalibration() is null,
            "data interruption cancels calibration while retaining the previous bias");
        session.Clear();
        Check(session.Output is null && !session.Status.Receiving, "suspend/disconnect clears angular velocity and active calibration");
        Check(session.Submit(Sample(0), ++now) && session.Output!.Gyroscope.Length < 1e-8, "fresh resumed input keeps the good bias and uses a new time epoch");
        var restored = new MotionSession(calibration, () => now);
        restored.Submit(Sample(0), now);
        Check(restored.Status.Calibrated && restored.Output!.Gyroscope.Length < 1e-8, "persisted bias can initialize the next physical session");
        var settings = AppSettings.FromJson("{}");
        Check(settings.DualSenseMotionEnabled && settings.MotionCalibration is null, "existing settings gain enhanced-only sensor input with no invented calibration");
        settings.MotionCalibration = new(" aa:bb:cc:dd:ee:ff ", calibration!.Value);
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = AppSettings.FromJson(json);
        Check(loaded.MotionCalibration?.Address == "AA:BB:CC:DD:EE:FF" && loaded.MotionCalibration.Bias == calibration && !json.Contains("IsFinite"),
            "calibration round-trips only its controller identity and finite physical bias");
        settings.MotionCalibration = new("AA:BB:CC:DD:EE:FF", new(500, 0, 0)); settings.Normalize();
        Check(settings.MotionCalibration is null, "corrupt or implausible calibration cannot disable aiming");
        settings.MotionCalibration = new("not-a-controller", default); settings.Normalize();
        Check(settings.MotionCalibration is null, "calibration requires an explicit physical identity");
        return passed;
    }
}
