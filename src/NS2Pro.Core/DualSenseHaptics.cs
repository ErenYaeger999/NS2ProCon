using System.Buffers.Binary;

namespace NS2Pro.Core;

/// <summary>Public VIIPER V5 wire format; implemented locally without linking backend libraries.</summary>
public static class DualSenseWire
{
    public const string DeviceType = "dualsensecombinedaudioduplexv5";
    public const int HeaderSize = 16, FeedbackSize = 474;

    public static byte[] Encode(byte type, uint sequence, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 4096) throw new ArgumentException("触觉帧过大");
        var result = new byte[HeaderSize + payload.Length];
        "VPCM"u8.CopyTo(result); result[4] = 5; result[5] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), (ushort)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), sequence);
        payload.CopyTo(result.AsSpan(HeaderSize));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), Crc(result.AsSpan(4, 8), payload));
        return result;
    }

    public static int PayloadLength(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderSize || !header[..4].SequenceEqual("VPCM"u8) || header[4] != 5)
            throw new InvalidDataException("触觉后端不是兼容的 VPCM V5 协议");
        int size = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
        if (size > 4096) throw new InvalidDataException("触觉后端帧长度无效");
        return size;
    }

    public static byte Validate(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, ref uint? previous)
    {
        if (PayloadLength(header) != payload.Length || BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) != Crc(header.Slice(4, 8), payload))
            throw new InvalidDataException("触觉后端帧校验失败");
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (previous.HasValue && sequence != unchecked(previous.Value + 1)) throw new InvalidDataException("触觉后端帧序号中断");
        previous = sequence;
        return header[5];
    }

    public static byte[] Input(XboxReport report)
    {
        var result = new byte[33];
        result[0] = Axis(report.LeftX); result[1] = Axis(-report.LeftY);
        result[2] = Axis(report.RightX); result[3] = Axis(-report.RightY);
        uint buttons = 0;
        foreach (var (source, target) in new (ushort, uint)[] {
            (0x1000,0x20),(0x2000,0x40),(0x4000,0x10),(0x8000,0x80),
            (0x100,0x100),(0x200,0x200),(0x20,0x1000),(0x10,0x2000),(0x40,0x4000),(0x80,0x8000) })
            if ((report.Buttons & source) != 0) buttons |= target;
        if (report.LeftTrigger > 0) buttons |= 0x400;
        if (report.RightTrigger > 0) buttons |= 0x800;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), buttons);
        result[8] = (byte)(report.Buttons & 15);
        result[9] = report.LeftTrigger; result[10] = report.RightTrigger;
        // No fabricated motion samples, touches, microphone or speaker output.
        return result;
    }
    private static byte Axis(int value) => unchecked((byte)(sbyte)Math.Clamp((int)Math.Round(value / 32767d * 127), -127, 127));

    private static uint Crc(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in header) crc = Byte(crc, value);
        foreach (byte value in payload) crc = Byte(crc, value);
        return ~crc;
        static uint Byte(uint crc, byte value) {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            return crc;
        }
    }
}

/// <summary>Experimental stereo PCM-to-parametric-LRA approximation, not lossless HD Rumble 2.</summary>
public sealed class DualSenseHaptics
{
    private readonly double[,] samples = new double[2, 64];
    private int position;
    private static readonly int[] LowFrequencies = [70, 100, 130, 160, 200];
    private static readonly int[] HighFrequencies = [250, 315, 400, 480];
    public HapticFrame Process(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length != 64) throw new InvalidDataException("原生触觉采样长度应为 64 字节");
        for (int i = 0; i < 32; i++) {
            samples[0, position] = unchecked((sbyte)pcm[i * 2]) / 128d;
            samples[1, position] = unchecked((sbyte)pcm[i * 2 + 1]) / 128d;
            position = (position + 1) % 64;
        }
        return new(Analyze(0), Analyze(1));
    }
    public void Reset() { Array.Clear(samples); position = 0; }
    private HapticMotor Analyze(int channel)
    {
        double mean = 0;
        for (int i = 0; i < 64; i++) mean += samples[channel, i] / 64;
        var low = Band(LowFrequencies); var high = Band(HighFrequencies);
        double total = Math.Max(1, low.Amplitude + high.Amplitude);
        return new(low.Amplitude / total, high.Amplitude / total, (ushort)low.Frequency, (ushort)high.Frequency);
        (int Frequency, double Amplitude) Band(int[] candidates) {
            double best = 0; int frequency = candidates[0];
            foreach (int f in candidates) {
                double real = 0, imaginary = 0, weight = 0;
                for (int i = 0; i < 64; i++) {
                    double window = .5 - .5 * Math.Cos(2 * Math.PI * i / 63), angle = 2 * Math.PI * f * i / 3000;
                    double value = (samples[channel, (position + i) % 64] - mean) * window;
                    real += value * Math.Cos(angle); imaginary += value * Math.Sin(angle); weight += window;
                }
                double amplitude = 2 * Math.Sqrt(real * real + imaginary * imaginary) / weight;
                if (amplitude > best) { best = amplitude; frequency = f; }
            }
            return (frequency, best < .002 ? 0 : Math.Min(best, 1));
        }
    }
}
