using NS2Pro.Core;
using Windows.Foundation;

namespace NS2Pro.App.Services;

internal static class NativeOperation
{
    public static Task<T> WaitAsync<T>(IAsyncOperation<T> operation, CancellationToken token,
        int seconds = 15, Action<T>? releaseLate = null) =>
        AsyncDeadline.WaitAsync(operation.AsTask(), TimeSpan.FromSeconds(seconds), token, releaseLate, operation.Cancel);
}
