using System.Diagnostics;

namespace NS2Pro.Core;

public sealed record HapticStatus(bool Ready, bool Enabled, bool Testing, bool Faulted, string Message,
    byte Low, byte High, long Feedback, long Writes, long Coalesced, double MaxWriteMilliseconds);

/// <summary>One writer, one latest state and an explicit stop barrier per physical connection.</summary>
public sealed class HapticSession : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly Func<byte[], CancellationToken, Task> write;
    private readonly Action<string>? warn;
    private readonly Task worker;
    private HapticFrame desired;
    private HapticFrame rich;
    private long richUntil;
    private HapticTest? test;
    private long testStarted, feedback, writes, coalesced;
    private bool enabled, inputAvailable, stopPending = true, stopping, faulted, dirty;
    private double strength, maxWriteMilliseconds;
    private byte low, high;
    private string message = "等待手柄输入";
    private const int IntervalMilliseconds = 15;

    public HapticSession(Func<byte[], CancellationToken, Task> write, bool enabled, double strength,
        Action<string>? warn = null)
    {
        this.write = write; this.enabled = enabled; this.strength = HapticProtocol.FiniteUnit(strength);
        this.warn = warn;
        worker = Task.Run(RunAsync);
    }

    public HapticStatus Status { get { lock (gate) return new(!stopping && !faulted && inputAvailable,
        enabled, test.HasValue, faulted, faulted ? message : stopping ? "震动连接已释放" :
        !inputAvailable ? "等待手柄输入" : !enabled ? "震动已关闭" : test.HasValue ? "正在测试震动" : "游戏震动已就绪",
        low, high, feedback, writes, coalesced, maxWriteMilliseconds); } }

    public void SubmitXbox(byte lowMotor, byte highMotor)
    {
        lock (gate) {
            if (stopping || faulted) return;
            feedback++; low = lowMotor; high = highMotor;
            if (!enabled || strength == 0 || !inputAvailable || test.HasValue) return;
            if (dirty) coalesced++;
            desired = HapticProtocol.FromXbox(lowMotor, highMotor); dirty = true;
            if (desired.IsSilent && Stopwatch.GetTimestamp() >= richUntil) stopPending = true;
        }
        Wake();
    }

    public void SubmitRich(HapticFrame frame)
    {
        lock (gate) {
            if (stopping || faulted || !enabled || strength == 0 || !inputAvailable || test.HasValue) return;
            if (dirty) coalesced++;
            rich = frame; richUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 12; // stream lease, ~83 ms
            dirty = true;
        }
        Wake();
    }

    public void Configure(bool value, double gain)
    {
        lock (gate) {
            enabled = value; strength = HapticProtocol.FiniteUnit(gain);
            if (!enabled || strength == 0) ClearNoLock();
            dirty = true;
        }
        Wake();
    }

    public void SetInputAvailable(bool value)
    {
        lock (gate) {
            if (inputAvailable == value) return;
            inputAvailable = value;
            // Returning input must never resurrect an earlier connection/input epoch's effect.
            ClearNoLock();
        }
        Wake();
    }

    public bool BeginTest(HapticTest pattern)
    {
        if (!Enum.IsDefined(pattern)) return false;
        lock (gate) {
            if (stopping || faulted || !inputAvailable || !enabled || strength == 0) return false;
            ClearNoLock(); test = pattern; testStarted = Stopwatch.GetTimestamp();
        }
        Wake(); return true;
    }

    public void Stop()
    {
        lock (gate) ClearNoLock();
        Wake();
    }

    private void ClearNoLock()
    {
        desired = default; rich = default; richUntil = 0; test = null; stopPending = true; dirty = true;
    }

    private void Wake()
    {
        try { signal.Release(); } catch (SemaphoreFullException) { }
    }

    private async Task RunAsync()
    {
        byte sequence = 0;
        bool lastActive = false;
        long lastSent = 0;
        try {
            while (true) {
                // Feedback may wake the loop, but cannot create parallel writes or defeat pacing.
                if (lastSent != 0) {
                    double left = IntervalMilliseconds - Stopwatch.GetElapsedTime(lastSent).TotalMilliseconds;
                    if (left > 0) await Task.Delay(TimeSpan.FromMilliseconds(left));
                }
                HapticFrame frame;
                double gain;
                bool forceStop, finish;
                lock (gate) {
                    finish = stopping;
                    forceStop = stopPending || finish;
                    if (forceStop) { frame = default; stopPending = false; }
                    else if (test is { } pattern) {
                        var elapsed = Stopwatch.GetElapsedTime(testStarted);
                        if (elapsed >= HapticProtocol.TestDuration(pattern)) { test = null; desired = default; frame = default; }
                        else frame = HapticProtocol.TestFrame(pattern, elapsed);
                    } else if (enabled && inputAvailable && Stopwatch.GetTimestamp() < richUntil) frame = rich;
                    else frame = enabled && inputAvailable ? desired : default;
                    gain = strength;
                    dirty = false;
                }
                bool active = !frame.IsSilent && gain > 0;
                if (forceStop || active || lastActive) {
                    long started = Stopwatch.GetTimestamp();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await write(HapticProtocol.EncodeBluetooth(frame, sequence++, gain), timeout.Token);
                    lastSent = Stopwatch.GetTimestamp(); lastActive = active;
                    lock (gate) {
                        writes++; maxWriteMilliseconds = Math.Max(maxWriteMilliseconds, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        message = test.HasValue ? "正在测试震动" : enabled ? "游戏震动已就绪" : "震动已关闭";
                    }
                }
                if (finish) return;
                // Constant output needs refreshes without callbacks. A lease can expire during
                // the awaited write, so send the final silence before parking the writer.
                bool animate;
                lock (gate) animate = lastActive || test.HasValue || !desired.IsSilent || Stopwatch.GetTimestamp() < richUntil || stopPending || stopping;
                if (!animate) await signal.WaitAsync();
            }
        } catch (Exception error) {
            lock (gate) {
                faulted = true; ClearNoLock(); message = "震动暂不可用，按键连接继续工作：" + error.Message;
            }
            // A write failure disables only this connection's haptics. Best-effort silence is bounded.
            try {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await write(HapticProtocol.EncodeBluetooth(default, sequence, 0), timeout.Token);
            } catch (Exception) { }
            try { warn?.Invoke(message); } catch (Exception) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate) { stopping = true; ClearNoLock(); }
        Wake();
        await worker;
        // Keep the small semaphore alive: a late unmanaged callback may still call SubmitXbox.
    }
}
