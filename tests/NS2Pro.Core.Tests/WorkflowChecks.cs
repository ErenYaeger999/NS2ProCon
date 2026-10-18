using System.Diagnostics;
using NS2Pro.Core;

internal static class WorkflowChecks
{
    public static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Workflow: " + message); passed++; }
        int platform = 0, enhanced = 0, common = 0;
        Task Platform(CancellationToken ct) { platform++; return Task.CompletedTask; }
        Task Common(CancellationToken ct) { common++; return Task.CompletedTask; }
        Task<bool> Enhanced(CancellationToken ct) { enhanced++; return Task.FromResult(true); }
        Check(!await ConnectionPreparation.RunAsync(false, Platform, Enhanced, Common, default) && platform == 1 && common == 1 && enhanced == 0,
            "ordinary connection performs one preparation, with no optional backend probe");
        platform = common = enhanced = 0;
        Check(await ConnectionPreparation.RunAsync(true, Platform, Enhanced, Common, default) && platform == 1 && enhanced == 1 && common == 0,
            "enhanced connection prepares once and never requires ViGEmBus");
        var preferences = new NS2Pro.App.Services.AppSettings { PreferNativeHaptics = true, OutputMode = NS2Pro.App.Services.GameOutputMode.DualSenseExperimental };
        string before = System.Text.Json.JsonSerializer.Serialize(preferences);
        bool effective = await ConnectionPreparation.RunAsync(true, Platform, ct => Task.FromResult(false), Common, default);
        Check(!effective && before == System.Text.Json.JsonSerializer.Serialize(preferences) && preferences.RequestedOutput == NS2Pro.App.Services.GameOutputMode.DualSenseExperimental,
            "temporary fallback preserves the requested output and serialized preference");
        using var cancel = new CancellationTokenSource();
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int afterCancel = 0;
        var preparation = ConnectionPreparation.RunAsync(true, async ct => { began.SetResult(); await Task.Delay(Timeout.Infinite, ct); },
            ct => { afterCancel++; return Task.FromResult(true); }, ct => { afterCancel++; return Task.CompletedTask; }, cancel.Token);
        await began.Task; cancel.Cancel();
        bool cancelled = false; try { await preparation; } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && afterCancel == 0, "cancelling the first preparation stage cannot start an output afterwards");
        using var duringBackend = new CancellationTokenSource();
        bool cancelledBackend = false;
        try { await ConnectionPreparation.RunAsync(true, ct => Task.CompletedTask,
            ct => { duringBackend.Cancel(); return Task.FromResult(false); }, ct => { afterCancel++; return Task.CompletedTask; }, duringBackend.Token); }
        catch (OperationCanceledException) { cancelledBackend = true; }
        Check(cancelledBackend && afterCancel == 0, "cancelled backend preparation never falls back or connects");

        var late = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        int released = 0, nativeCancelled = 0;
        bool timedOut = false;
        try { await AsyncDeadline.WaitAsync(late.Task, TimeSpan.FromMilliseconds(20), default, _ => Interlocked.Increment(ref released), () => nativeCancelled++); }
        catch (TimeoutException) { timedOut = true; }
        late.SetResult(new object()); await HapticChecks.Until(() => Volatile.Read(ref released) == 1);
        Check(timedOut && nativeCancelled == 1 && released == 1, "deadline requests native cancellation and releases exactly one late owned result");
        released = 0;
        var owned = new object();
        Check(ReferenceEquals(await AsyncDeadline.WaitAsync(Task.FromResult(owned), TimeSpan.FromSeconds(1), default, _ => released++), owned) && released == 0,
            "successful native result transfers ownership to its caller");
        using var cancelledWait = new CancellationTokenSource();
        var lateCancelled = new TaskCompletionSource<object>(); cancelledWait.Cancel();
        cancelled = false;
        try { await AsyncDeadline.WaitAsync(lateCancelled.Task, TimeSpan.FromSeconds(1), cancelledWait.Token, _ => released++); }
        catch (OperationCanceledException) { cancelled = true; }
        lateCancelled.SetResult(new object()); await HapticChecks.Until(() => released == 1);
        Check(cancelled, "user cancellation stays cancellation rather than becoming a timeout");

        var performance = new InputPerformance();
        long now = Stopwatch.GetTimestamp();
        for (int i = 0; i < 67; i++) performance.RecordReport(now - (long)((66 - i) * .015 * Stopwatch.Frequency));
        var sample = performance.Snapshot;
        Check(Math.Abs(sample.Hertz - 66.6667) < .01 && Math.Abs(sample.IntervalMilliseconds - 15) < .01,
            "observed cadence measures real 15ms reports without inventing USB poll rates");
        Check(sample.ForwardMedianMilliseconds is null, "no fabricated latency appears before a completed output submission");
        performance.RecordForward(Stopwatch.GetTimestamp() - Stopwatch.Frequency / 100);
        Check(performance.Snapshot.ForwardMedianMilliseconds is >= 10, "forward timing includes elapsed host queue and submission time");
        performance.Reset();
        Check(performance.Snapshot.Hertz == 0 && performance.Snapshot.ForwardMedianMilliseconds is null, "disconnect and reconnect clear prior session measurements");
        Check(DiagnosticPrivacy.Redact(@"C:\Users\Alice\logs BC:89:A6:5C:7A:32") == @"%USERPROFILE%\logs **:**:**:**:**:**",
            "export removes user profile names and complete controller addresses");
        Check(DiagnosticPrivacy.Redact("Windows 10.0.26100 127.0.0.1:32452") == "Windows 10.0.26100 127.0.0.1:32452", "redaction retains useful OS and loopback diagnostics");
        return passed;
    }
}
