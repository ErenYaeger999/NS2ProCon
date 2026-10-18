using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NS2Pro.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace NS2Pro.App.Services;

internal static class DiagnosticService
{
    public static async Task<string> CollectAsync(AppSettings settings, ControllerService controller,
        EnhancementService enhancement, IEnumerable<string> logs)
    {
        var metrics = controller.Performance;
        var copy = new StringBuilder()
            .AppendLine($"{AppIdentity.DisplayName} v{typeof(DiagnosticService).Assembly.GetName().Version}")
            .AppendLine($"采集时间：{DateTimeOffset.Now:O}")
            .AppendLine($"系统：{Environment.OSVersion} · {RuntimeInformation.OSArchitecture}")
            .AppendLine($"构建：{FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).ProductVersion}")
            .AppendLine($"状态：{controller.Info}")
            .AppendLine($"选择模式：{settings.RequestedOutput} · 本次模式：{controller.ActiveOutputMode}")
            .AppendLine($"布局：{settings.Layout} · 死区：{settings.Deadzone:P0} · 震动：{settings.RumbleEnabled}/{settings.RumbleStrength:P0}")
            .AppendLine($"增强管理：{settings.ManageHapticBackend} · 端口：{EnhancementService.Port(settings)}")
            .AppendLine($"组件目标版本：VIIPER {EnhancementComponents.BackendVersion} · USBIP {EnhancementComponents.DriverVersion}")
            .AppendLine($"增强状态：{enhancement.Status.Phase} · {enhancement.Status.Message}\n{enhancement.Status.Detail}")
            .AppendLine($"回报率：{metrics.Hertz:F1} Hz · 报告间隔：{metrics.IntervalMilliseconds:F2} ms")
            .AppendLine($"本机转发：中位 {Time(metrics.ForwardMedianMilliseconds)} · P95 {Time(metrics.ForwardP95Milliseconds)} · {metrics.Samples} 样本（不代表端到端延迟）");
        try {
            var adapters = await NativeOperation.WaitAsync(DeviceInformation.FindAllAsync(BluetoothAdapter.GetDeviceSelector(),
                new[] { "System.Devices.DeviceInstanceId" }), CancellationToken.None, 8);
            foreach (var adapter in adapters) {
                copy.AppendLine("蓝牙适配器：" + adapter.Name);
                try {
                    // Device-interface properties do not contain the parent's installed driver.
                    // Raw property keys match DEVPKEY_Device_DriverVersion / DriverProvider in the Windows SDK.
                    const string version = "{A8B865DD-2E3D-4094-AD97-E593A70C75D6} 3", provider = "{A8B865DD-2E3D-4094-AD97-E593A70C75D6} 9";
                    if (!adapter.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var instance) || instance is not string id)
                        throw new IOException("系统未返回设备实例");
                    var device = await NativeOperation.WaitAsync(DeviceInformation.CreateFromIdAsync(id,
                        new[] { version, provider, "System.Devices.DeviceManufacturer" }, DeviceInformationKind.Device), CancellationToken.None, 3);
                    foreach (var (key, label) in new[] { (version, "驱动版本"), (provider, "驱动提供者"), ("System.Devices.DeviceManufacturer", "制造商") })
                        copy.AppendLine($"  {label}：{(device.Properties.TryGetValue(key, out var value) && value is not null ? value : "系统未返回")}");
                } catch (Exception error) { copy.AppendLine("  驱动信息未返回：" + error.Message); }
            }
            if (adapters.Count == 0) copy.AppendLine("蓝牙适配器：未发现");
        } catch (Exception error) { copy.AppendLine("蓝牙硬件信息读取未完成：" + error.Message); }
        copy.AppendLine("\n近期日志（默认隐藏用户目录和手柄地址）：");
        foreach (string line in logs) copy.AppendLine(line);
        return DiagnosticPrivacy.Redact(copy.ToString());
    }
    private static string Time(double? value) => value is { } time ? $"{time:F2} ms" : "—";
}
