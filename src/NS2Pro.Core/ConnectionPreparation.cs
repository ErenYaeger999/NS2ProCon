namespace NS2Pro.Core;

/// <summary>Prepare exactly one effective output, preserving the caller's preference.</summary>
public static class ConnectionPreparation
{
    public static async Task<bool> RunAsync(bool preferEnhanced,
        Func<CancellationToken, Task> platform,
        Func<CancellationToken, Task<bool>> enhanced,
        Func<CancellationToken, Task> common,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await platform(token);
        token.ThrowIfCancellationRequested();
        if (preferEnhanced) {
            bool ready = await enhanced(token);
            token.ThrowIfCancellationRequested();
            if (ready) return true;
        }
        await common(token);
        token.ThrowIfCancellationRequested();
        return false;
    }
}
