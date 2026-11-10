using System.Diagnostics;

namespace NS2Pro.Core;

public enum InputReportSource { Native, Common }
public enum InputStreamAction { Waiting, Receiving, Recover, Stop, Closed }
public sealed record InputStreamSnapshot(bool Open, InputReportSource Source, long Reports, long NativeReports,
    long CommonReports, double LastInputAgeSeconds, double LongestGapMilliseconds, string Recovery, string EndReason);

/// <summary>Lifetime, ownership and bounded recovery for one physical BLE connection.
/// Late callbacks retain this object and cannot update a replacement session.</summary>
public sealed class InputStreamSession : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime;
    private readonly Func<long> now;
    private TaskCompletionSource updated = Signal();
    private long selectedAt, lastInputAt, endedAt, reports, nativeReports, commonReports;
    private double longestGap;
    private bool closed, recoveryAttempted, transportLost;
    private InputReportSource source;
    private Exception? failure;
    private string recovery = "尚未尝试", endReason = "";
    public CancellationToken Token { get; }
    public bool IsOpen { get { lock (gate) return !closed && !Token.IsCancellationRequested; } }
    public bool TransportLost { get { lock (gate) return transportLost; } }
    public Exception? Failure { get { lock (gate) return failure; } }
    public bool RecoveryAttempted { get { lock (gate) return recoveryAttempted; } }
    public InputReportSource Source { get { lock (gate) return source; } }
    public InputStreamAction NextAction {
        get { lock (gate) {
            var state = Snapshot;
            if (!state.Open) return InputStreamAction.Closed;
            if (reports == 0) return state.LastInputAgeSeconds >= 10 ? InputStreamAction.Stop : InputStreamAction.Waiting;
            if (lastInputAt > 0 && state.LastInputAgeSeconds <= 1) return InputStreamAction.Receiving;
            if (!recoveryAttempted && state.LastInputAgeSeconds >= 3) return InputStreamAction.Recover;
            if (recoveryAttempted && state.LastInputAgeSeconds >= 6) return InputStreamAction.Stop;
            return InputStreamAction.Waiting;
        } }
    }

    public InputStreamSession(CancellationToken token = default, Func<long>? timestamp = null)
    {
        now = timestamp ?? Stopwatch.GetTimestamp; selectedAt = now();
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token); Token = lifetime.Token;
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Accepts(InputReportSource incoming)
    {
        lock (gate) return !closed && !Token.IsCancellationRequested && incoming == source;
    }

    public bool Record(InputReportSource incoming, long at)
    {
        lock (gate) {
            if (!Accepts(incoming) || at <= 0 || at < lastInputAt) return false;
            if (lastInputAt > 0) longestGap = Math.Max(longestGap, Stopwatch.GetElapsedTime(lastInputAt, at).TotalMilliseconds);
            lastInputAt = at; reports++;
            if (incoming == InputReportSource.Native) nativeReports++; else commonReports++;
            var previous = updated; updated = Signal(); previous.TrySetResult();
            return true;
        }
    }

    public void Select(InputReportSource selected)
    {
        lock (gate) {
            if (!IsOpen) return;
            source = selected; selectedAt = now(); lastInputAt = 0;
        }
    }

    public void ArmInputDeadline() { lock (gate) { if (lastInputAt == 0) selectedAt = now(); } }

    public InputStreamSnapshot Snapshot
    {
        get { lock (gate) {
            double age = Math.Max(0, Stopwatch.GetElapsedTime(lastInputAt == 0 ? selectedAt : lastInputAt, closed ? endedAt : now()).TotalSeconds);
            return new(IsOpen, source, reports, nativeReports, commonReports, age,
                lastInputAt > 0 ? Math.Max(longestGap, age * 1000) : longestGap, recovery, endReason);
        } }
    }

    public async Task<bool> WaitForInputAsync(long after, TimeSpan timeout, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, token);
        linked.CancelAfter(timeout);
        try {
            while (true) {
                Task signal;
                lock (gate) {
                    linked.Token.ThrowIfCancellationRequested();
                    if (reports > after && lastInputAt > 0) return true;
                    signal = updated.Task;
                }
                await signal.WaitAsync(linked.Token).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (!Token.IsCancellationRequested && !token.IsCancellationRequested) { return false; }
    }

    public async Task<bool> RecoverAsync(Func<CancellationToken, Task> disable, Func<CancellationToken, Task> enable,
        TimeSpan firstInputTimeout)
    {
        lock (gate) {
            Token.ThrowIfCancellationRequested();
            if (recoveryAttempted || !IsOpen) return false;
            recoveryAttempted = true; recovery = "停用当前通知";
        }
        try {
            await disable(Token).ConfigureAwait(false);
            Token.ThrowIfCancellationRequested();
            long before;
            lock (gate) { before = reports; recovery = "重新订阅通知"; }
            await enable(Token).ConfigureAwait(false);
            Token.ThrowIfCancellationRequested();
            lock (gate) recovery = "等待有效输入首帧";
            bool received = await WaitForInputAsync(before, firstInputTimeout, Token).ConfigureAwait(false);
            lock (gate) recovery = received ? "已收到有效输入，恢复成功" : "订阅完成但未收到有效输入";
            if (!received) End("重新订阅后未收到有效输入");
            return received;
        } catch (Exception error) {
            lock (gate) recovery = "未完成 · " + recovery;
            End("输入恢复未完成", error: error);
            throw;
        }
    }

    public void End(string reason, bool lost = false, Exception? error = null)
    {
        lock (gate) {
            if (lost) transportLost = true;
            if (closed) return;
            closed = true; endedAt = now(); endReason = reason; failure = error;
            updated.TrySetResult();
        }
        // Run continuations outside our lock; teardown may need an output or GATT lock.
        try { lifetime.Cancel(); } catch (AggregateException) { }
    }

    public void Dispose() { End("连接已结束"); lifetime.Dispose(); }
}
