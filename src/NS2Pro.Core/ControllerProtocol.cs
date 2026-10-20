using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace NS2Pro.Core;

public enum ButtonLayout { Labels, Positions }
public enum AdvertisementKind { Discovery, Reconnect, WakeConsole, Unknown }
public readonly record struct Stick(double X, double Y);
public readonly record struct StickCalibration(Stick Center, Stick Maximum, Stick Minimum);
public readonly record struct ControllerAdvertisement(AdvertisementKind Kind, ulong TargetAddress);

[StructLayout(LayoutKind.Sequential)]
public struct XboxReport
{
    public ushort Buttons;
    public byte LeftTrigger, RightTrigger;
    public short LeftX, LeftY, RightX, RightY;
}

public sealed record ControllerInput(XboxReport Xbox, Stick Left, Stick Right, Stick UnfilteredLeft, Stick UnfilteredRight,
    byte RightButtons, byte LeftButtons, byte ExtraButtons, int BatteryPercent, bool IsCharging = false, bool HasExternalPower = false);

/// <summary>Pure protocol code. No radio, pairing or flash write operations.</summary>
public static class ControllerProtocol
{
    public const ushort ManufacturerId = 0x0553;
    public const ushort VendorId = 0x057e;
    public const ushort ProductId = 0x2069;

    // Manufacturer payload excludes the two-byte Bluetooth company identifier.
    public static ControllerAdvertisement? ParseAdvertisement(ReadOnlySpan<byte> data)
    {
        if (data.Length < 17 || data[0] != 1 || data[1] != 0 || data[2] != 3 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[3..]) != VendorId ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[5..]) != ProductId) return null;
        ulong target = 0;
        for (int i = 0; i < 6; i++) target |= (ulong)data[10 + i] << (i * 8);
        var kind = data[9] == 0x81 ? AdvertisementKind.WakeConsole :
            data[9] != 0 ? AdvertisementKind.Unknown :
            target == 0 ? AdvertisementKind.Discovery : AdvertisementKind.Reconnect;
        return new(kind, target);
    }

    public static Stick UnpackStick(ReadOnlySpan<byte> bytes) =>
        bytes.Length < 3 ? throw new ArgumentException("摇杆数据不完整") :
        new(bytes[0] | ((bytes[1] & 15) << 8), (bytes[1] >> 4) | (bytes[2] << 4));

    public static StickCalibration? ParseCalibration(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 9) return null;
        var center = UnpackStick(bytes);
        var max = UnpackStick(bytes[3..]);
        var min = UnpackStick(bytes[6..]);
        if (center.X is < 256 or > 3840 || center.Y is < 256 or > 3840 ||
            max.X is < 128 or > 4095 || max.Y is < 128 or > 4095 ||
            min.X is < 128 or > 4095 || min.Y is < 128 or > 4095) return null;
        return new(center, max, min);
    }

    public static Stick Normalize(Stick raw, StickCalibration calibration, double deadzone)
    {
        deadzone = Math.Clamp(deadzone, 0, .3);
        var dx = raw.X - calibration.Center.X;
        var dy = raw.Y - calibration.Center.Y;
        var x = Math.Clamp(dx / (dx >= 0 ? calibration.Maximum.X : calibration.Minimum.X), -1, 1);
        var y = Math.Clamp(dy / (dy >= 0 ? calibration.Maximum.Y : calibration.Minimum.Y), -1, 1);
        var length = Math.Sqrt(x * x + y * y);
        if (length <= deadzone) return new(0, 0);
        var gain = Math.Min(1, (length - deadzone) / (1 - deadzone)) / length;
        return new(x * gain, y * gain);
    }

    public static ControllerInput Decode(ReadOnlySpan<byte> data, StickCalibration leftCalibration,
        StickCalibration rightCalibration, ButtonLayout layout, double deadzone = .06)
    {
        if (data.Length < 11) throw new ArgumentException("Pro 输入报告不完整");
        byte right = data[2], left = data[3], extra = data[4];
        ushort buttons = 0;
        // Report 0x09: B/A/Y/X = bits 0/1/2/3. XInput: A/B/X/Y = 12/13/14/15.
        ushort[] face = layout == ButtonLayout.Labels ?
            [0x2000, 0x1000, 0x8000, 0x4000] : [0x1000, 0x2000, 0x4000, 0x8000];
        for (int i = 0; i < 4; i++) if ((right & (1 << i)) != 0) buttons |= face[i];
        if ((right & 0x10) != 0) buttons |= 0x0200;
        if ((right & 0x40) != 0) buttons |= 0x0010;
        if ((right & 0x80) != 0) buttons |= 0x0080;
        if ((left & 0x01) != 0) buttons |= 0x0002;
        if ((left & 0x02) != 0) buttons |= 0x0008;
        if ((left & 0x04) != 0) buttons |= 0x0004;
        if ((left & 0x08) != 0) buttons |= 0x0001;
        if ((left & 0x10) != 0) buttons |= 0x0100;
        if ((left & 0x40) != 0) buttons |= 0x0020;
        if ((left & 0x80) != 0) buttons |= 0x0040;
        var ls = Normalize(UnpackStick(data[5..]), leftCalibration, deadzone);
        var rs = Normalize(UnpackStick(data[8..]), rightCalibration, deadzone);
        var report = new XboxReport {
            Buttons = buttons, LeftTrigger = (byte)((left & 0x20) != 0 ? 255 : 0),
            RightTrigger = (byte)((right & 0x20) != 0 ? 255 : 0),
            LeftX = (short)Math.Round(ls.X * 32767), LeftY = (short)Math.Round(ls.Y * 32767),
            RightX = (short)Math.Round(rs.X * 32767), RightY = (short)Math.Round(rs.Y * 32767)
        };
        return new(report, ls, rs,
            Normalize(UnpackStick(data[5..]), leftCalibration, 0), Normalize(UnpackStick(data[8..]), rightCalibration, 0),
            // Pro report Power Info: bit 0 = external power, bit 1 = charging, bits 2–5 = level.
            // https://github.com/ndeadly/switch2_controller_research/blob/master/hid_reports.md#input-report-0x09
            right, left, extra, Math.Clamp(((data[1] >> 2) & 15) * 10, 0, 100),
            (data[1] & 2) != 0, (data[1] & 1) != 0);
    }

    public static byte[] CalibrationReadRequest(uint address)
    {
        // Calibration reads memory with 02/04; no flash write or pairing command.
        byte[] request = [0x02, 0x91, 0x01, 0x04, 0, 8, 0, 0, 11, 0x7e, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), address);
        return request;
    }

    public static ControllerInput DecodeCommon(ReadOnlySpan<byte> data, StickCalibration leftCalibration,
        StickCalibration rightCalibration, ButtonLayout layout, double deadzone, byte powerInfo)
    {
        if (data.Length < 16) throw new ArgumentException("公共输入报告不完整");
        // BLE 0x05 buttons/sticks -> the same logical state used by our 0x09 decoder.
        Span<byte> pro = stackalloc byte[11]; pro.Clear(); pro[0] = data[0]; pro[1] = powerInfo;
        uint buttons = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        foreach (var (source, target, bit) in new (int, int, byte)[] {
            (0,2,4),(1,2,8),(2,2,1),(3,2,2),(6,2,16),(7,2,32),(9,2,64),(10,2,128),
            (16,3,1),(18,3,2),(19,3,4),(17,3,8),(22,3,16),(23,3,32),(8,3,64),(11,3,128),
            (12,4,1),(13,4,2),(24,4,4),(25,4,8),(14,4,16) })
            if ((buttons & (1u << source)) != 0) pro[target] |= bit;
        data.Slice(10, 6).CopyTo(pro[5..]);
        return Decode(pro, leftCalibration, rightCalibration, layout, deadzone);
    }

    public static bool IsPressed(string name, ControllerInput? state)
    {
        int r=state?.RightButtons??0,l=state?.LeftButtons??0,e=state?.ExtraButtons??0;
        return name switch {
            "B"=>(r&1)!=0,"A"=>(r&2)!=0,"Y"=>(r&4)!=0,"X"=>(r&8)!=0,"R"=>(r&16)!=0,"ZR"=>(r&32)!=0,"+"=>(r&64)!=0,"RS"=>(r&128)!=0,
            "↓"=>(l&1)!=0,"→"=>(l&2)!=0,"←"=>(l&4)!=0,"↑"=>(l&8)!=0,"L"=>(l&16)!=0,"ZL"=>(l&32)!=0,"−"=>(l&64)!=0,"LS"=>(l&128)!=0,
            "HOME"=>(e&1)!=0,"截图" or "□"=>(e&2)!=0,"GR"=>(e&4)!=0,"GL"=>(e&8)!=0,"C"=>(e&16)!=0,_=>false
        };
    }

    public static byte[] ParseCalibrationResponse(ReadOnlySpan<byte> response, uint address)
    {
        if (response.Length < 27 || response[0] != 2 || response[1] != 1 || response[3] != 4 ||
            response[8] != 11 || BinaryPrimitives.ReadUInt32LittleEndian(response[12..]) != address)
            throw new InvalidDataException("手柄返回的校准数据与请求不符");
        return response.Slice(16, 11).ToArray();
    }
}
