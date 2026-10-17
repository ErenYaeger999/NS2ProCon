namespace NS2Pro.Core;

// Frequencies are Nintendo protocol codes, not a promise of measured actuator Hz.
public readonly record struct HapticMotor(double LowAmplitude, double HighAmplitude,
    ushort LowFrequency = 225, ushort HighFrequency = 320)
{
    public bool IsSilent => !(double.IsFinite(LowAmplitude) && LowAmplitude > 0) && !(double.IsFinite(HighAmplitude) && HighAmplitude > 0);
}

public readonly record struct HapticFrame(HapticMotor Left, HapticMotor Right)
{
    public bool IsSilent => Left.IsSilent && Right.IsSilent;
    public static HapticFrame Silent => default;
}

public enum HapticTest { Low, High, Left, Right, Click, Pulses }

/// <summary>Switch 2 Pro BLE vibration, separate from USB HID output reports.</summary>
public static class HapticProtocol
{
    // A conservative software ceiling, not a manufacturer-certified continuous-output limit.
    public const int AmplitudeCeiling = 500;
    public const int PacketSize = 33;
    public static readonly Guid BluetoothUuid = new("cc483f51-9258-427d-a939-630c31f72b05");

    public static HapticFrame FromXbox(byte low, byte high)
    {
        // XInput describes two frequency bands, not independent spatial L/R effects.
        // Keep both bands on both LRAs; preserve zero exactly and lift weak feedback modestly.
        var motor = new HapticMotor(Math.Pow(low / 255d, .9), Math.Pow(high / 255d, .9));
        return new(motor, motor);
    }

    public static HapticFrame TestFrame(HapticTest test, TimeSpan elapsed)
    {
        double seconds = elapsed.TotalSeconds;
        bool on = test switch { HapticTest.Click => seconds < .06,
            HapticTest.Pulses => seconds < .9 && seconds % .3 < .07, _ => seconds < .65 };
        if (!on) return default;
        var low = new HapticMotor(.55, 0);
        var high = new HapticMotor(0, .55);
        var mixed = new HapticMotor(.35, .35);
        return test switch { HapticTest.Low => new(low, low), HapticTest.High => new(high, high),
            HapticTest.Left => new(mixed, default), HapticTest.Right => new(default, mixed),
            _ => new(mixed, mixed) };
    }

    public static TimeSpan TestDuration(HapticTest test) => TimeSpan.FromSeconds(test switch {
        HapticTest.Click => .12, HapticTest.Pulses => 1, _ => .75 });

    public static byte[] EncodeBluetooth(HapticFrame frame, byte sequence, double strength)
    {
        var packet = new byte[PacketSize];
        WriteMotor(packet.AsSpan(1, 16), frame.Left, sequence, strength);
        WriteMotor(packet.AsSpan(17, 16), frame.Right, sequence, strength);
        return packet;
    }

    private static void WriteMotor(Span<byte> output, HapticMotor motor, byte sequence, double strength)
    {
        output[0] = (byte)(0x50 | (sequence & 15)); // enabled, one operation, 4-bit sequence
        ulong value = (ulong)(uint)Math.Clamp((int)motor.LowFrequency, 0, 511) |
            ((ulong)Amplitude(motor.LowAmplitude, strength) << 10) |
            ((ulong)(uint)Math.Clamp((int)motor.HighFrequency, 0, 511) << 20) |
            ((ulong)Amplitude(motor.HighAmplitude, strength) << 30);
        for (int i = 0; i < 5; i++) output[i + 1] = (byte)(value >> (8 * i));
        // Two unused operations remain zero. No pairing, flash, LED or wake commands.
    }

    private static int Amplitude(double amplitude, double strength) =>
        (int)Math.Round(FiniteUnit(amplitude) * FiniteUnit(strength) * AmplitudeCeiling);
    internal static double FiniteUnit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
