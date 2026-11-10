namespace NS2Pro.Core;

/// <summary>Serialize state-changing native requests. A timed-out request may still change
/// the device later, so its lane cannot admit a compensating or subsequent request.</summary>
public sealed class StatefulOperationGate
{
    private readonly SemaphoreSlim serial = new(1, 1);
    private Exception? failure;
    private Task pending = Task.CompletedTask;
    public bool IsUsable => Volatile.Read(ref failure) is null;
    public string? FailedStage { get; private set; }
    public Task WhenSettled => Volatile.Read(ref pending);

    public async Task<T> RunAsync<T>(string stage, Func<Task<T>> start, TimeSpan timeout,
        CancellationToken token, Action? cancelNative = null, Action<T>? releaseLate = null)
    {
        await serial.WaitAsync(token).ConfigureAwait(false);
        try {
            token.ThrowIfCancellationRequested();
            if (!IsUsable) throw new IOException("上一次蓝牙操作的状态无法确认，请重新连接手柄。", failure);
            try {
                // Creation is inside the lane, after the lifetime and failure checks.
                var operation = start();
                var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref pending, settled.Task);
                _ = operation.ContinueWith(completed => {
                    if (!completed.IsCompletedSuccessfully) {
                        _ = completed.Exception;
                        settled.TrySetResult();
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                var result = await AsyncDeadline.WaitAsync(operation, timeout, token, value => {
                    // A late discovery result can still own native objects. Reconnection
                    // must wait for their release, not just for the native task to finish.
                    try { releaseLate?.Invoke(value); }
                    finally { settled.TrySetResult(); }
                }, cancelNative).ConfigureAwait(false);
                settled.TrySetResult();
                return result;
            } catch (Exception error) when (error is TimeoutException or OperationCanceledException or ObjectDisposedException ||
                error.HResult == unchecked((int)0x80000013)) {
                FailedStage = stage;
                Volatile.Write(ref failure, error);
                throw;
            }
        } finally { serial.Release(); }
    }

    public static async Task WaitForSettlementAsync(Task previous, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { await previous.WaitAsync(timeout, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        // The previous request's fault belongs to the previous session. Only settlement,
        // not its result, is required before creating a new connection.
        catch (Exception) when (previous.IsCompleted) { }
    }
}
