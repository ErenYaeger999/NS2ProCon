namespace NS2Pro.Core;

/// <summary>A deadline owns the wait; abandoned native results still own resources.</summary>
public static class AsyncDeadline
{
    public static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout, CancellationToken token,
        Action<T>? releaseLate = null, Action? cancelNative = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try { return await task.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) {
            try { cancelNative?.Invoke(); } catch (Exception) { }
            _ = task.ContinueWith(completed => {
                if (completed.IsCompletedSuccessfully) { try { releaseLate?.Invoke(completed.Result); } catch (Exception) { } }
                else _ = completed.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("操作超时，请重试连接。");
        }
    }
}
