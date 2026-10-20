using System.Buffers.Binary;
using System.Diagnostics;

namespace NS2Pro.Core;

public readonly record struct MotionVector(double X, double Y, double Z)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    [System.Text.Json.Serialization.JsonIgnore]
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public static MotionVector operator +(MotionVector a, MotionVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static MotionVector operator -(MotionVector a, MotionVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static MotionVector operator /(MotionVector a, double b) => new(a.X / b, a.Y / b, a.Z / b);
}

// Axes use the SDL/PlayStation Y-up convention. Units are degrees/s and g, not HID integers.
public sealed record MotionState(MotionVector Gyroscope, MotionVector Acceleration, long ReceivedAt);
public sealed record MotionReading(uint Sequence, uint DeviceTimestamp, double Temperature,
    MotionVector Gyroscope, MotionVector Acceleration);
public sealed record MotionCalibrationProfile(string Address, MotionVector Bias);
public sealed record MotionStatus(bool Receiving, bool Calibrating, bool Calibrated, double Progress,
    string Message, MotionState? State, double Hertz, long Samples);

/// <summary>NS2 Pro BLE report 0x05 / FD2 facts. Report 0x09 has a different packed IMU format.</summary>
public static class MotionProtocol
{
    public static readonly Guid InputUuid = new("ab7de9be-89fe-49ad-828f-118f09df7fd2");
    public const int FreshnessMilliseconds = 120;

    public static MotionReading? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 60) return null;
        // Nintendo -> SDL/PlayStation: X, Z, -Y. The Pro gain is distinct from Joy-Con 2.
        // Cross-checked against SDL Switch2 and Switch2Connect; independently implemented.
        const double gyroScale = .07, accelScale = 1d / 4096;
        return new(BinaryPrimitives.ReadUInt32LittleEndian(data), BinaryPrimitives.ReadUInt32LittleEndian(data[42..]),
            25 + Read(data, 46) / 126.9,
            new(Read(data, 54) * gyroScale, Read(data, 58) * gyroScale, -Read(data, 56) * gyroScale),
            new(Read(data, 48) * accelScale, Read(data, 52) * accelScale, -Read(data, 50) * accelScale));
    }
    private static double Read(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));

    public static byte[] FeatureRequest(byte subcommand)
    {
        // Common reports also need buttons/sticks/rumble retained; stop disables only IMU.
        if (subcommand is not (2 or 4 or 5)) throw new ArgumentOutOfRangeException(nameof(subcommand));
        return [0x0c, 0x91, 1, subcommand, 0, 4, 0, 0, subcommand == 5 ? (byte)4 : (byte)0x27, 0, 0, 0];
    }

    public static byte[] SelectReportRequest(byte report)
    {
        if (report is not (5 or 9)) throw new ArgumentOutOfRangeException(nameof(report));
        return [3, 0x91, 1, 0x0a, 0, 4, 0, 0, report, 0, 0, 0];
    }

    // Volatile report transport start (03/0D), using an invalid host address. Not 03/07-09 pairing.
    public static byte[] StartPublicReportsRequest() => [3, 0x91, 1, 0x0d, 0, 8, 0, 0, 1, 0, 255, 255, 255, 255, 255, 255];

    public static bool IsFresh(MotionState? state, long now) => state is not null && state.ReceivedAt > 0 &&
        state.ReceivedAt <= now && Stopwatch.GetElapsedTime(state.ReceivedAt, now).TotalMilliseconds <= FreshnessMilliseconds &&
        state.Gyroscope.IsFinite && state.Acceleration.IsFinite;

    public static bool ValidCalibration(MotionVector bias) => bias.IsFinite && bias.Length <= 20;
}

/// <summary>One physical session. No mouse output, resampling, aim curves or automatic moving calibration.</summary>
public sealed class MotionSession(MotionVector? savedBias = null, Func<long>? timestamp = null)
{
    private readonly object gate = new();
    private readonly Func<long> now = timestamp ?? Stopwatch.GetTimestamp;
    private readonly long startedAt = timestamp?.Invoke() ?? Stopwatch.GetTimestamp();
    private readonly Queue<long> times = new();
    private MotionVector bias = savedBias is { } value && MotionProtocol.ValidCalibration(value) ? value : default;
    private bool calibrated = savedBias is { } initial && MotionProtocol.ValidCalibration(initial);
    private MotionState? state;
    private MotionReading? previous;
    private long samples, calibrationStarted, stableStarted;
    private int stableCount;
    private MotionVector sum, squareSum, accelerationSum;
    private bool calibrating;
    private double progress;
    private MotionVector? completed;
    private string message = "正在等待体感数据…";

    public bool Submit(ReadOnlySpan<byte> packet, long receivedAt)
    {
        var reading = MotionProtocol.Decode(packet);
        if (reading is null) return false;
        lock (gate) {
            bool fresh = MotionProtocol.IsFresh(state, receivedAt);
            if (fresh && previous is not null) {
                uint delta = unchecked(reading.Sequence - previous.Sequence);
                if (delta == 0 || delta >= 0x80000000) return false;
            }
            if (receivedAt <= 0 || (state is not null && receivedAt <= state.ReceivedAt)) return false;
            // An uninitialised/masked FD2 block contains zero acceleration, not gravity.
            if (reading.Acceleration.Length < .05) return false;
            if (!fresh) { times.Clear(); ResetWindow(); }
            var old = previous; previous = reading;
            samples++; times.Enqueue(receivedAt);
            while (times.Count > 512 || (times.Count > 1 && Stopwatch.GetElapsedTime(times.Peek(), receivedAt).TotalSeconds > 2)) times.Dequeue();
            if (calibrating) {
                if (Stopwatch.GetElapsedTime(calibrationStarted, receivedAt).TotalSeconds > 12) {
                    calibrating = false; progress = 0; message = "校准未完成，请将手柄放稳后重试；保留上次校准。";
                } else {
                    bool stable = reading.Gyroscope.Length < 15 && reading.Acceleration.Length is > .8 and < 1.2 &&
                        (!fresh || old is null || ((reading.Gyroscope - old.Gyroscope).Length < 1.5 &&
                            (reading.Acceleration - old.Acceleration).Length < .04));
                    if (!stable) { ResetWindow(); message = "检测到移动，请将手柄静置在桌面上…"; }
                    else {
                        if (stableStarted == 0) stableStarted = receivedAt;
                        stableCount++; sum += reading.Gyroscope; accelerationSum += reading.Acceleration;
                        var g = reading.Gyroscope; squareSum += new MotionVector(g.X * g.X, g.Y * g.Y, g.Z * g.Z);
                        progress = Math.Clamp(Stopwatch.GetElapsedTime(stableStarted, receivedAt).TotalSeconds / 2, 0, 1);
                        message = "正在校准，请保持手柄静止…";
                        if (progress >= 1 && stableCount >= 20) {
                            var mean = sum / stableCount; var meanSquare = squareSum / stableCount;
                            double variance = meanSquare.X + meanSquare.Y + meanSquare.Z - mean.X * mean.X - mean.Y * mean.Y - mean.Z * mean.Z;
                            if (variance > .12 || !MotionProtocol.ValidCalibration(mean) || (accelerationSum / stableCount).Length < .8) {
                                ResetWindow(); message = "手柄还在晃动，请放稳后继续…";
                            } else {
                                bias = mean; completed = mean; calibrated = true; calibrating = false;
                                message = "校准完成 · 已应用本次静止零点";
                            }
                        }
                    }
                }
            }
            state = new(calibrating ? default : reading.Gyroscope - bias, reading.Acceleration, receivedAt);
            return true;
        }
    }

    public bool BeginCalibration()
    {
        lock (gate) {
            if (!MotionProtocol.IsFresh(state, now())) return false;
            calibrating = true; calibrationStarted = now(); completed = null; ResetWindow();
            message = "将手柄静置在桌面上，保持约 2 秒…";
            state = state! with { Gyroscope = default };
            return true;
        }
    }

    public MotionVector? TakeCalibration() { lock (gate) { var value = completed; completed = null; return value; } }

    public MotionState? Output { get { lock (gate) return MotionProtocol.IsFresh(state, now()) ? state : null; } }

    public MotionStatus Status
    {
        get { lock (gate) {
            bool receiving = MotionProtocol.IsFresh(state, now());
            if (!receiving && calibrating) { calibrating = false; progress = 0; message = "体感数据中断，校准已取消；保留上次校准。"; }
            double duration = times.Count < 2 ? 0 : Stopwatch.GetElapsedTime(times.Peek(), times.Last()).TotalSeconds;
            return new(receiving, calibrating, calibrated, progress,
                !receiving ? samples == 0 ? message == "正在等待体感数据…" && Stopwatch.GetElapsedTime(startedAt, now()).TotalSeconds > 3 ?
                    "尚未收到体感数据，按键与震动可继续使用；请断开后重试。" : message : "体感数据暂时中断，请重新连接手柄。" :
                    calibrating || message.StartsWith("校准") ? message : calibrated ? "六轴已就绪 · 由游戏或 Steam 使用" : "六轴已就绪 · 首次使用建议静置校准",
                receiving ? state : null, receiving && duration > 0 ? (times.Count - 1) / duration : 0, samples);
        } }
    }

    public void Clear(string reason = "等待新的体感数据…")
    {
        lock (gate) { state = null; previous = null; times.Clear(); calibrating = false; ResetWindow(); message = reason; }
    }
    public void Unavailable(string reason) { lock (gate) { Clear(reason); samples = 0; } }
    private void ResetWindow() { stableStarted = 0; stableCount = 0; progress = 0; sum = squareSum = accelerationSum = default; }
}
