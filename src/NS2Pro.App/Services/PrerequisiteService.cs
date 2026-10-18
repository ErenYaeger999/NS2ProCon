using NS2Pro.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace NS2Pro.App.Services;

public sealed record EnvironmentItem(string Name, bool Ready, string Detail);
public sealed record EnvironmentReport(EnvironmentItem System, EnvironmentItem Bluetooth, EnvironmentItem Driver)
{
    public bool Ready => System.Ready && Bluetooth.Ready && Driver.Ready;
    public IEnumerable<EnvironmentItem> Items => [System, Bluetooth, Driver];
}

public static class PrerequisiteService
{
    private static int installing;
    public static bool Installing => Volatile.Read(ref installing) != 0;
    private static string RestartFile => Path.Combine(AppSettings.DataDirectory, "vigem-restart.json");
    public static bool RestartRequired => RebootRequirement.IsPending(RestartFile);

    public static async Task<EnvironmentReport> CheckAsync(GameOutputMode mode = GameOutputMode.Xbox, CancellationToken token = default)
    {
        var version = Environment.OSVersion.Version;
        var issue = SystemRequirements.GetIssue(version, RuntimeInformation.OSArchitecture);
        var system = new EnvironmentItem("Windows", issue is null, issue ?? $"Windows {(version.Build >= 22000 ? "11" : "10")} · x64 · {version.Build}");
        var bluetoothTask = CheckBluetoothAsync(token);
        var driverTask = mode == GameOutputMode.Xbox ? CheckCommonDriverAsync(token) : Task.FromResult(
            new EnvironmentItem("增强游戏输出", true, "增强组件将在连接时检测；未就绪时尝试通用兼容。可到游戏模式查看组件状态。"));
        await Task.WhenAll(bluetoothTask, driverTask);
        return new(system, await bluetoothTask, await driverTask);
    }

    public static async Task<EnvironmentItem> CheckCommonDriverAsync(CancellationToken token = default)
    {
        if (Installing) return new("游戏手柄驱动", false, "官方驱动正在安装，请等待完成。");
        if (RestartRequired) return new("游戏手柄驱动", false, "驱动安装已完成，请重启电脑后连接，无需重复安装。");
        try {
            await AsyncDeadline.WaitAsync(Task.Run(() => { VirtualGamepad.EnsureDriverAvailable(); return true; }), TimeSpan.FromSeconds(8), token);
            return new("游戏手柄驱动", true, "ViGEmBus 已就绪");
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (DllNotFoundException) { return new("游戏手柄驱动", false, "程序文件不完整，请重新运行完整安装包。"); }
        catch (Exception error) { return new("游戏手柄驱动", false, error.Message); }
    }
    private static async Task<EnvironmentItem> CheckBluetoothAsync(CancellationToken token)
    {
        try {
            var detail = await ControllerService.CheckBluetoothAsync(token: token);
            return new("蓝牙", true, detail);
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) { return new("蓝牙", false, error is TimeoutException ? "蓝牙检查超时，请稍后重新检查。" : error.Message); }
    }

    public static async Task<string> InstallDriverAsync()
    {
        if (AppSettings.IsPreview) return "引导预览不会安装驱动，请正常打开应用或运行安装包。";
        if (Interlocked.CompareExchange(ref installing, 1, 0) != 0) return "驱动正在安装，请等待完成。";
        try {
        if (RestartRequired) return "驱动已安装，需要重启电脑后再连接，无需重复安装。";
        // Only run the immutable, bundled vendor installer after checking its pinned digest.
        var folder = Path.Combine(AppContext.BaseDirectory, "Prerequisites");
        await using var manifestStream = typeof(PrerequisiteService).Assembly.GetManifestResourceStream("NS2Pro.App.Prerequisites.json")
            ?? throw new InvalidDataException($"驱动清单缺失，请重新安装{AppIdentity.DisplayName}。");
        var manifest = await JsonSerializer.DeserializeAsync<PrerequisiteFile[]>(manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"驱动清单缺失，请重新安装{AppIdentity.DisplayName}。");
        var driver = manifest.Single(x => x.Id == "vigembus");
        if (Path.GetFileName(driver.File) != driver.File) throw new InvalidDataException("驱动文件名无效。");
        var installer = Path.Combine(folder, driver.File);
        string hash;
        await using (var installerStream = File.OpenRead(installer)) hash = Convert.ToHexString(await SHA256.HashDataAsync(installerStream));
        if (!hash.Equals(driver.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("驱动安装文件校验失败，请重新下载安装包。");
        using var process = Process.Start(new ProcessStartInfo(installer, "/norestart") { UseShellExecute = true, Verb = "runas" })
            ?? throw new InvalidOperationException("无法启动驱动安装程序。");
        await process.WaitForExitAsync();
        if (process.ExitCode is 3010 or 1641) RebootRequirement.Mark(RestartFile);
        return process.ExitCode switch {
            0 => "驱动安装已完成，请重新检查环境。",
            3010 or 1641 => "驱动已安装，需要重启电脑后再连接手柄。",
            _ => $"驱动安装未完成（{process.ExitCode}）。可重新运行完整安装包，或联系作者。"
        };
        } finally { Volatile.Write(ref installing, 0); }
    }

    private sealed record PrerequisiteFile(string Id, string File, string Sha256);
}
