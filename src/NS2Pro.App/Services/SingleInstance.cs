using System.Runtime.Versioning;

namespace NS2Pro.App.Services;

/// <summary>Session-wide ownership and activation, acquired before WinUI starts.</summary>
[SupportedOSPlatform("windows")]
internal sealed class SingleInstance : IDisposable
{
    internal const string InstanceName = "Local\\NS2Pro.NativeApp";
    private readonly EventWaitHandle activation;
    private readonly List<Mutex> handles = [];
    private readonly List<Mutex> owned = [];
    private RegisteredWaitHandle? registration;
    private bool disposed;
    public bool IsPrimary { get; }

    public SingleInstance(string name = InstanceName)
    {
        // Create this before taking either lock: even a duplicate during cold startup leaves
        // an activation pending until the primary window is ready to register its callback.
        activation = new(false, EventResetMode.AutoReset, name + ".Activate");
        try {
            // Reserve the old preview lock as well, so old and new binaries share one instance.
            foreach (var key in new[] { name, name + ".Preview" }) {
                var mutex = new Mutex(false, key); handles.Add(mutex);
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) { ReleaseOwnership(); return; }
                owned.Add(mutex);
            }
            IsPrimary = true;
        } catch { Dispose(); throw; }
    }

    public void RequestActivation() => activation.Set();

    public void Listen(Action activate)
    {
        if (!IsPrimary || disposed) throw new InvalidOperationException("只有运行中的主实例能接收激活请求");
        if (registration is not null) throw new InvalidOperationException("已注册实例激活回调");
        registration = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => activate(), null, Timeout.Infinite, false);
    }

    private void ReleaseOwnership()
    {
        for (int i = owned.Count - 1; i >= 0; i--) owned[i].ReleaseMutex();
        owned.Clear();
    }

    // Mutex ownership is thread-affine. Program keeps this scope on its original STA thread.
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        registration?.Unregister(null);
        ReleaseOwnership();
        foreach (var mutex in handles) mutex.Dispose();
        activation.Dispose();
    }
}
