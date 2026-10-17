using System.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Media.Devices;

namespace NS2Pro.App.Services;

/// <summary>Verify the newly created controller's own audio container, without changing default audio.</summary>
public static class HapticEnvironmentProbe
{
    private const string Container = "System.Devices.ContainerId";
    public static async Task VerifyAsync(int port, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20)); token = deadline.Token;
        string hidSelector = HidDevice.GetDeviceSelector(1, 5, 0x054c, 0x0ce6);
        var before = await DeviceInformation.FindAllAsync(hidSelector, new[] { Container }).AsTask(token);
        var existing = before.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var device = await ViiperGamepad.CreateAsync(port, token);
        device.Update(default);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(15)) {
            token.ThrowIfCancellationRequested();
            if (!device.FeedbackAvailable) throw new IOException("触觉后端连接中断，请重新检测。");
            var controllers = await DeviceInformation.FindAllAsync(hidSelector, new[] { Container }).AsTask(token);
            var containers = controllers.Where(d => !existing.Contains(d.Id) && d.Properties.ContainsKey(Container))
                .Select(d => d.Properties[Container]?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (containers.Count > 0) {
                var renderers = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector(), new[] { Container }).AsTask(token);
                if (renderers.Any(d => d.Properties.TryGetValue(Container, out var id) && containers.Contains(id?.ToString()))) return;
            }
            await Task.Delay(250, token);
        }
        throw new IOException("Windows 尚未准备好虚拟手柄的触觉音频通道。若刚安装驱动，请先重启；否则稍后重新检测。");
    }
}
