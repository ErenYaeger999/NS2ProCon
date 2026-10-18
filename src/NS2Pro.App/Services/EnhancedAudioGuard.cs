using System.Diagnostics;
using NS2Pro.Core;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;

namespace NS2Pro.App.Services;

/// <summary>A short-lived owner around one virtual controller, including its teardown.</summary>
internal sealed class EnhancedAudioGuard : IAsyncDisposable
{
    private const string ContainerProperty = "System.Devices.ContainerId";
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task worker;
    private readonly Action<string> log;
    private readonly TaskCompletionSource identified = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile string message = "正在记住原音频设备…";
    public string Message => message;
    public bool Ready => identified.Task.IsCompletedSuccessfully;
    private AudioEndpoint[] ownedEndpoints = [];
    private readonly HashSet<string> originalIds = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<AudioEndpoint> OwnedEndpoints => Volatile.Read(ref ownedEndpoints);
    public EnhancedAudioGuard(Action<string>? log = null)
    {
        this.log = log ?? (text => AppLog.Write("info", text));
        worker = Task.Run(RunAsync);
    }

    public static async Task<EnhancedAudioGuard?> StartAsync(CancellationToken token)
    {
        var guard = new EnhancedAudioGuard();
        try { await guard.initialized.Task.WaitAsync(token); return guard; }
        catch (OperationCanceledException) { await guard.DisposeAsync(); throw; }
        catch (Exception error) {
            AppLog.Write("warn", "未能记住音频设备，可在声音设置中手动选择：" + error.Message);
            await guard.DisposeAsync(); return null;
        }
    }

    public async Task WaitForProtectionAsync(CancellationToken token)
    {
        // Audio failures never prevent a working controller connection.
        try { await identified.Task.WaitAsync(TimeSpan.FromSeconds(8), token); }
        catch (TimeoutException) { }
        catch (Exception) when (!token.IsCancellationRequested) { }
    }

    private static async Task<Dictionary<string, Guid>> ControllersAsync(CancellationToken token)
    {
        var devices = await DeviceInformation.FindAllAsync(HidDevice.GetDeviceSelector(1, 5, 0x054c, 0x0ce6),
            new[] { ContainerProperty }).AsTask(token);
        return devices.Where(d => d.Properties.TryGetValue(ContainerProperty, out var value) && Guid.TryParse(value?.ToString(), out var id) && id != Guid.Empty)
            .ToDictionary(d => d.Id, d => Guid.Parse(d.Properties[ContainerProperty].ToString()!), StringComparer.OrdinalIgnoreCase);
    }

    private async Task RunAsync()
    {
        WindowsAudioDefaults? audio = null;
        AudioDefaultPolicy? policy = null;
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            audio = new();
            var defaults = audio.ReadDefaults();
            foreach (var id in defaults.Values.OfType<string>()) originalIds.Add(id);
            policy = new(defaults);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            startup.CancelAfter(TimeSpan.FromSeconds(5));
            var before = await ControllersAsync(startup.Token);
            initialized.TrySetResult();
            message = "已记住原音频设备，等待虚拟手柄…";
            Guid container = Guid.Empty;
            var clock = Stopwatch.StartNew();
            List<AudioEndpoint> endpoints = [];
            long lastCatalog = 0;
            while (!stop.IsCancellationRequested) {
                if (container == Guid.Empty) {
                    var after = await ControllersAsync(stop.Token);
                    var added = after.Where(d => !before.ContainsKey(d.Key)).Select(d => d.Value).Distinct().ToArray();
                    if (added.Length == 1) container = added[0];
                    // Ambiguous simultaneous discovery must not touch unrelated audio devices.
                    else if (added.Length > 1) throw new IOException("同时出现多个新 DualSense，无法确认本次音频设备，请手动选择声音设备。");
                    if (container == Guid.Empty && clock.Elapsed.TotalSeconds > 15) throw new IOException("未能识别本次虚拟手柄的音频设备，请手动检查声音设置。");
                }
                if (container != Guid.Empty) {
                    if (lastCatalog == 0 || !Ready || Stopwatch.GetElapsedTime(lastCatalog).TotalSeconds >= 1) {
                        endpoints = audio.Endpoints(); lastCatalog = Stopwatch.GetTimestamp();
                    }
                    foreach (var endpoint in endpoints.Where(e => e.Container == container)) owned.Add(endpoint.Id);
                    Volatile.Write(ref ownedEndpoints, endpoints.Where(e => e.Container == container).ToArray());
                    if (owned.Count > 0) {
                        Restore(audio, policy, owned, endpoints);
                        message = "正在保持原来的声音与麦克风设备";
                        if (OwnedEndpoints.Select(e => e.Flow).Distinct().Count() == 2) identified.TrySetResult();
                    } else if (clock.Elapsed.TotalSeconds > 15) throw new IOException("触觉音频端点尚未就绪，请手动检查声音设置。");
                }
                await Task.Delay(250, stop.Token);
            }
        } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) {
            message = "音频自动保持暂不可用，请手动检查声音设置";
            initialized.TrySetException(error); identified.TrySetException(error);
            try { log(message + "：" + error.Message); } catch (Exception) { }
        } finally {
            // The virtual output is disposed before this guard, but Windows may remove its
            // audio endpoints later. Only touch a default still pointing to our old endpoint.
            if (audio is not null) {
                try { if (policy is not null && owned.Count > 0) Restore(audio, policy, owned, audio.Endpoints()); }
                catch (Exception error) { try { log("音频收尾恢复未完成：" + error.Message); } catch (Exception) { } }
                audio.Dispose();
            }
            initialized.TrySetCanceled(); identified.TrySetCanceled();
        }
    }

    private void Restore(WindowsAudioDefaults audio, AudioDefaultPolicy policy, HashSet<string> owned, List<AudioEndpoint> endpoints)
    {
        var active = endpoints.Where(e => e.Active && (e.Container != Guid.Empty || originalIds.Contains(e.Id)))
            .Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var change in policy.Reconcile(audio.ReadDefaults(), owned, active))
            if (audio.Restore(change)) log($"已保持原音频设备 · {change.Slot.Flow}/{change.Slot.Role}");
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); await worker;
    }
}
