using System.Diagnostics;
using NS2Pro.App.Services;
using NS2Pro.Core;

internal static class BluetoothLifetimeChecks
{
    public static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
        static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        static async Task<Exception?> Failure(Task task) { try { await task; return null; } catch (Exception e) { return e; } }
        var small = TimeSpan.FromMilliseconds(30);
        var generous = TimeSpan.FromSeconds(3);

        // Native Cancel can acknowledge our wait without settling the real Windows operation.
        var queue = new StatefulOperationGate();
        var notify = Pending<int>(); int starts = 0, nativeCancels = 0;
        var first = queue.RunAsync("Notify", () => { starts++; return notify.Task; }, small, default, () => nativeCancels++);
        var restore = queue.RunAsync("None", () => { starts++; return Task.FromResult(0); }, generous, default);
        Check(await Failure(first) is TimeoutException && nativeCancels == 1, "Notify timeout requests native cancellation without claiming it completed");
        Check(await Failure(restore) is IOException && starts == 1 && !queue.IsUsable && queue.FailedStage == "Notify",
            "a queued None cannot race an unresolved Notify after its deadline");
        var settlement = StatefulOperationGate.WaitForSettlementAsync(queue.WhenSettled, generous, default);
        Check(!settlement.IsCompleted, "reconnection waits for the real abandoned native request, not the expired caller wait");
        notify.SetResult(0); await Task.Yield();
        await settlement;
        Check(await Failure(queue.RunAsync("late recovery", () => { starts++; return Task.FromResult(0); }, generous, default)) is IOException && starts == 1,
            "late successful Notify never reopens the abandoned operation lane");
        var freshQueue = new StatefulOperationGate();
        Check(await freshQueue.RunAsync("new connection", () => Task.FromResult(7), generous, default) == 7,
            "a fresh connection has independent native operation ownership");
        var unending = Pending<int>();
        Check(await Failure(StatefulOperationGate.WaitForSettlementAsync(unending.Task, small, default)) is TimeoutException,
            "unsettled Windows work bounds reconnection waiting without issuing another request");
        using (var waitCancellation = new CancellationTokenSource()) {
            var waiting = StatefulOperationGate.WaitForSettlementAsync(unending.Task, generous, waitCancellation.Token);
            waitCancellation.Cancel();
            Check(await Failure(waiting) is OperationCanceledException, "the previous-request fence remains cancellable");
        }
        unending.SetException(new ObjectDisposedException("old session completed with a fault"));
        await StatefulOperationGate.WaitForSettlementAsync(unending.Task, generous, default);
        Check(unending.Task.IsCompleted, "a settled old error is observed and cannot poison a new session");

        queue = new(); var ownedLate = Pending<IDisposable>(); int released = 0;
        var discovery = queue.RunAsync("service discovery", () => ownedLate.Task, small, default,
            releaseLate: value => { value.Dispose(); released++; });
        Check(await Failure(discovery) is TimeoutException && !queue.IsUsable,
            "service discovery uses the same pending-request fence as notification writes");
        ownedLate.SetResult(new CancellationTokenSource());
        await HapticChecks.Until(() => Volatile.Read(ref released) == 1);
        Check(released == 1, "a late discovery result releases owned native resources exactly once");

        using (var allowRelease = new ManualResetEventSlim()) {
            queue = new(); ownedLate = Pending<IDisposable>(); released = 0;
            var releasing = Pending<bool>();
            discovery = queue.RunAsync("late resource cleanup", () => ownedLate.Task, small, default,
                releaseLate: value => {
                    releasing.TrySetResult(true);
                    try { allowRelease.Wait(TimeSpan.FromSeconds(5)); }
                    finally { value.Dispose(); Interlocked.Increment(ref released); }
                });
            Check(await Failure(discovery) is TimeoutException, "the abandoned discovery retains a cleanup settlement fence");
            ownedLate.SetResult(new CancellationTokenSource());
            try {
                await releasing.Task.WaitAsync(generous);
                Check(ownedLate.Task.IsCompleted && !queue.WhenSettled.IsCompleted,
                    "a completed native task cannot admit reconnection while late resource release is still running");
            } finally { allowRelease.Set(); }
            await queue.WhenSettled.WaitAsync(generous);
            Check(released == 1, "reconnection opens only after the late owned resource has been released once");
        }

        using (var cancelled = new CancellationTokenSource()) {
            cancelled.Cancel(); starts = 0;
            Check(await Failure(freshQueue.RunAsync("cancelled before factory", () => { starts++; return Task.FromResult(0); }, generous, cancelled.Token)) is OperationCanceledException && starts == 0,
                "an already-cancelled session never creates a native request");
        }
        queue = new(); var pending = Pending<int>(); var started = Pending<bool>();
        using (var cancelled = new CancellationTokenSource()) {
            var writing = queue.RunAsync("in flight", () => { started.SetResult(true); return pending.Task; }, generous, cancelled.Token);
            await started.Task; cancelled.Cancel();
            Check(await Failure(writing) is OperationCanceledException && !queue.IsUsable,
                "disconnect during native write forbids follow-up configuration on that session");
            pending.SetException(new ObjectDisposedException("late WinRT result")); await Task.Yield();
        }
        queue = new();
        Check(await Failure(queue.RunAsync<int>("disposed characteristic", () => throw new ObjectDisposedException("GATT"), generous, default)) is ObjectDisposedException && !queue.IsUsable,
            "disposed native factory is retained as the failure and closes its lane");

        // The healthy path is serialized too; the second factory cannot start eagerly.
        queue = new(); pending = Pending<int>(); starts = 0;
        first = queue.RunAsync("one", () => { starts++; return pending.Task; }, generous, default);
        var second = queue.RunAsync("two", () => { starts++; return Task.FromResult(2); }, generous, default);
        Check(starts == 1, "only one state-changing GATT operation can be in flight");
        pending.SetResult(1);
        Check(await first == 1 && await second == 2 && starts == 2 && queue.IsUsable, "settled operations admit their ordered successor");

        long now = Stopwatch.GetTimestamp();
        using var stream = new InputStreamSession(timestamp: () => now);
        now += 9 * Stopwatch.Frequency;
        stream.ArmInputDeadline();
        Check(stream.NextAction == InputStreamAction.Waiting, "device discovery time is not misclassified as a first-input timeout");
        stream.Record(InputReportSource.Native, now);
        Check(stream.NextAction == InputStreamAction.Receiving, "real native input establishes receiving state");
        now += 2 * Stopwatch.Frequency;
        Check(stream.NextAction == InputStreamAction.Waiting, "a brief gap yields waiting rather than invented low-rate input");
        now += Stopwatch.Frequency;
        Check(stream.NextAction == InputStreamAction.Recover, "a persistent gap requests one controlled recovery");
        Check(!stream.Record(InputReportSource.Common, now) && stream.Snapshot.Reports == 1, "an unselected report channel cannot steal input ownership");
        stream.Select(InputReportSource.Common);
        Check(stream.Accepts(InputReportSource.Common) && !stream.Accepts(InputReportSource.Native), "only the explicitly selected FD2 channel forwards controls");
        // FD2 remains selected while ordinary power reports are ignored for game controls.
        Check(!stream.Record(InputReportSource.Native, now) && stream.Record(InputReportSource.Common, now),
            "native power-only or late reports never discard working common input");

        // None/Notify acknowledgements alone do not prove that usable controls returned.
        using (var ackOnly = new InputStreamSession()) {
            ackOnly.Record(InputReportSource.Native, Stopwatch.GetTimestamp());
            bool recovered = await ackOnly.RecoverAsync(_ => Task.CompletedTask, _ => Task.CompletedTask, small);
            Check(!recovered && !ackOnly.IsOpen && !ackOnly.Snapshot.Recovery.Contains("恢复成功"),
                "successful CCCD ACK without a valid new input frame is a closed failed recovery");
        }
        using (var healthy = new InputStreamSession()) {
            healthy.Select(InputReportSource.Common); healthy.Record(InputReportSource.Common, Stopwatch.GetTimestamp());
            int calls = 0;
            bool recovered = await healthy.RecoverAsync(_ => { calls++; return Task.CompletedTask; }, _ => {
                calls++; healthy.Record(InputReportSource.Common, Stopwatch.GetTimestamp()); return Task.CompletedTask;
            }, generous);
            Check(recovered && calls == 2 && healthy.IsOpen && healthy.Snapshot.Recovery.Contains("恢复成功"),
                "a real FD2 frame received before Notify returns still verifies recovery");
            Check(!await healthy.RecoverAsync(_ => { calls++; return Task.CompletedTask; }, _ => Task.CompletedTask, generous) && calls == 2,
                "one physical connection never loops re-subscriptions indefinitely");
        }
        using (var wrongChannel = new InputStreamSession()) {
            wrongChannel.Select(InputReportSource.Common);
            bool recovered = await wrongChannel.RecoverAsync(_ => Task.CompletedTask, _ => {
                wrongChannel.Record(InputReportSource.Native, Stopwatch.GetTimestamp()); return Task.CompletedTask;
            }, small);
            Check(!recovered && wrongChannel.Snapshot.Reports == 0, "unselected native frames cannot falsely verify FD2 recovery");
        }

        foreach (bool failNotify in new[] { false, true }) {
            using var failing = new InputStreamSession();
            failing.Record(InputReportSource.Native, Stopwatch.GetTimestamp());
            var lane = new StatefulOperationGate(); var late = Pending<int>(); int notifyCalls = 0;
            async Task Disable(CancellationToken ct) {
                await lane.RunAsync("None", () => failNotify ? Task.FromResult(0) : late.Task, small, ct);
            }
            async Task Enable(CancellationToken ct) {
                notifyCalls++; await lane.RunAsync("Notify", () => late.Task, small, ct);
            }
            Check(await Failure(failing.RecoverAsync(Disable, Enable, generous)) is TimeoutException,
                (failNotify ? "Notify" : "None") + " timeout remains distinct from first-frame timeout");
            Check(!failing.IsOpen && !lane.IsUsable && notifyCalls == (failNotify ? 1 : 0),
                "timed-out configuration closes the session without starting an overlapping compensation");
            late.SetResult(0);
            Check(!failing.Record(InputReportSource.Native, Stopwatch.GetTimestamp()), "late native callback after failed recovery cannot revive the old session");
        }
        foreach (bool lost in new[] { false, true }) {
            using var ending = new InputStreamSession(); int enabled = 0;
            var disabled = Pending<bool>(); var unblock = Pending<bool>();
            var recovering = ending.RecoverAsync(async _ => { disabled.SetResult(true); await unblock.Task; },
                _ => { enabled++; return Task.CompletedTask; }, generous);
            await disabled.Task; ending.End(lost ? "蓝牙失联" : "用户取消", lost); unblock.SetResult(true);
            Check(await Failure(recovering) is OperationCanceledException && enabled == 0,
                "disconnect/cancel between None and Notify cannot create another GATT request");
        }
        using (var waiting = new InputStreamSession()) {
            var wait = waiting.WaitForInputAsync(0, generous, default);
            waiting.End("失联", lost: true);
            Check(await Failure(wait) is OperationCanceledException, "link loss interrupts a pending first-frame wait promptly");
        }
        using (var midNotify = new InputStreamSession()) {
            var lane = new StatefulOperationGate(); var lateNotify = Pending<int>(); var beganNotify = Pending<bool>();
            var recovery = midNotify.RecoverAsync(_ => Task.CompletedTask, async ct => {
                await lane.RunAsync("Notify", () => { beganNotify.SetResult(true); return lateNotify.Task; }, generous, ct);
            }, generous);
            await beganNotify.Task; midNotify.End("订阅期间蓝牙断开", lost: true);
            Check(await Failure(recovery) is OperationCanceledException && !lane.IsUsable && !midNotify.IsOpen,
                "disconnect inside Notify cancels recovery and quarantines its still-pending native request");
            lateNotify.SetResult(0);
            Check(!midNotify.Record(InputReportSource.Native, Stopwatch.GetTimestamp()), "a late Notify completion cannot revive a closed device's controls");
        }
        using (var midFrame = new InputStreamSession()) {
            var enabled = Pending<bool>();
            var recovery = midFrame.RecoverAsync(_ => Task.CompletedTask, _ => { enabled.SetResult(true); return Task.CompletedTask; }, generous);
            await enabled.Task; midFrame.End("首帧等待期间取消");
            Check(await Failure(recovery) is OperationCanceledException && !midFrame.Snapshot.Recovery.Contains("恢复成功"),
                "disconnect after Notify ACK cannot be mistaken for a first-frame success");
        }
        using (var disposedNotify = new InputStreamSession()) {
            var error = new ObjectDisposedException("expired GATT characteristic");
            var recovery = disposedNotify.RecoverAsync(_ => Task.CompletedTask, _ => throw error, generous);
            Check(ReferenceEquals(await Failure(recovery), error) && ReferenceEquals(disposedNotify.Failure, error) && !disposedNotify.IsOpen,
                "the reported disposed-object failure closes recovery and retains its original diagnostic exception");
        }
        using (var replacement = new InputStreamSession()) {
            stream.End("上一轮已结束"); stream.Dispose();
            Check(!stream.Record(InputReportSource.Common, ++now) && replacement.Record(InputReportSource.Native, Stopwatch.GetTimestamp()) && replacement.Snapshot.Reports == 1,
                "old callbacks after teardown never advance a replacement connection's count or source");
            stream.End("迟到的断开事件", lost: true);
            Check(stream.TransportLost && replacement.IsOpen, "late status events only affect their original session");
        }
        using (var stalledAgain = new InputStreamSession(timestamp: () => now)) {
            stalledAgain.Record(InputReportSource.Native, now);
            await stalledAgain.RecoverAsync(_ => Task.CompletedTask, _ => {
                stalledAgain.Record(InputReportSource.Native, ++now); return Task.CompletedTask;
            }, generous);
            now += 7 * Stopwatch.Frequency;
            Check(stalledAgain.NextAction == InputStreamAction.Stop && stalledAgain.Snapshot.LongestGapMilliseconds >= 7000,
                "a second persistent stall is bounded and diagnostics include the final unreturned gap");
            stalledAgain.End("已结束"); double ageAtEnd = stalledAgain.Snapshot.LastInputAgeSeconds;
            now += 100 * Stopwatch.Frequency;
            Check(stalledAgain.Snapshot.LastInputAgeSeconds == ageAtEnd, "export preserves input age at teardown rather than time spent after disconnect");
        }
        using (var neverStarted = new InputStreamSession(timestamp: () => now)) {
            now += 11 * Stopwatch.Frequency;
            Check(neverStarted.NextAction == InputStreamAction.Stop, "no first input cannot leave an indefinitely linked waiting state");
        }
        using (var errorSession = new InputStreamSession()) {
            var error = new ObjectDisposedException("WinRT GATT"); errorSession.End("底层对象已失效", lost: true, error: error);
            Check(ReferenceEquals(errorSession.Failure, error) && errorSession.Token.IsCancellationRequested,
                "transport errors retain original diagnostic detail while cancelling dependent waits");
            Check(new ConnectionAttempt { Stage = "接收游戏输入" }.Describe(error).Contains("蓝牙会话已失效"),
                "expired-session UI explains retry instead of exposing an English disposed-object message");
        }
        return passed;
    }
}
