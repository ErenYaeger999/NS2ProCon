using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Win32;

namespace NS2Pro.App.Services;

public sealed class EnhancementService(Func<int, CancellationToken, Task> validateHost) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private OwnedBackendProcess? backend;
    private volatile EnhancementStatus status = new(EnhancementPhase.Unchecked, "先检测已有组件，缺少时再提示安装。");
    private bool hostVerified;
    public EnhancementStatus Status => status;
    private static string Root => Path.Combine(AppSettings.DataDirectory, "Enhancement");
    private static string ComponentRoot => Path.Combine(Root, "Backend");
    private static string BackendPath => Path.Combine(ComponentRoot, EnhancementComponents.BackendVersion, "viiper.exe");
    private static string DriverExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "USBip", "usbip.exe");
    private static string RestartMarker => Path.Combine(Root, "driver-restart.json");
    public static int Port(AppSettings settings) => settings.ManageHapticBackend ? EnhancementComponents.ApiPort : settings.ViiperPort;
    private void Set(EnhancementPhase phase, string message, double progress = 0, string detail = "") => status = new(phase, message, progress, detail);
    public void Cancel()
    {
        if (status.Phase == EnhancementPhase.InstallingDriver) return;
        try { operation?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public async Task<bool> PrepareAsync(AppSettings settings, bool install = false, CancellationToken token = default, bool verifyAgain = false)
    {
        if (AppSettings.IsPreview) return false;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await gate.WaitAsync(caller.Token);
        try {
            operation = CancellationTokenSource.CreateLinkedTokenSource(caller.Token);
            token = operation.Token;
            if (verifyAgain) hostVerified = false;
            Set(EnhancementPhase.Checking, "正在检查增强环境…");
            if (!settings.ManageHapticBackend) {
                try { await ViiperGamepad.ProbeAsync(settings.ViiperPort, token); }
                catch (IOException error) {
                    Set(EnhancementPhase.Error, "其他软件的后端未启动。可改用本应用自动管理。", detail:
                        $"当前设置仅连接本机端口 {settings.ViiperPort}，不会启动后端。安装组件或重启电脑不会改变此设置。" + error.Message);
                    AppLog.Write("warn", "外部增强后端不可用：" + error.Message); return false;
                }
                Set(EnhancementPhase.Validating, "正在验证已有后端的虚拟手柄和触觉音频…");
                await validateHost(settings.ViiperPort, token);
                Set(EnhancementPhase.Ready, "已有后端已就绪，可连接手柄后启动游戏。"); return true;
            }
            Directory.CreateDirectory(Root);
            if (AwaitingRestart()) { Set(EnhancementPhase.RestartRequired, "组件已安装，请先重启电脑，再重新检测。"); return false; }
            string? version = ReadInstalledDriverVersion();
            bool driverReady = false;
            string driverDetail = "未找到 USBIP 命令行程序。";
            if (version == EnhancementComponents.DriverVersion && File.Exists(DriverExe)) {
                try {
                    var cliVersion = await RunReadOnlyAsync(DriverExe, "--version", token);
                    var ports = await RunReadOnlyAsync(DriverExe, "port", token);
                    driverReady = cliVersion.Trim() == EnhancementComponents.DriverVersion && !HasAbiFailure(ports);
                    driverDetail = $"USBIP 版本：{cliVersion.Trim()}\n{ports.Trim()}";
                } catch (Exception error) when (error is not OperationCanceledException) {
                    driverDetail = error.Message; AppLog.Write("warn", "增强驱动检查未通过：" + error.Message);
                }
            }
            bool filesReady = await EnhancementComponents.IsBackendReadyAsync(Path.GetDirectoryName(BackendPath)!, token);
            var issue = EnhancementStatus.PrerequisiteIssue(version, driverReady, filesReady, driverDetail);
            if (issue is not null && (!install || issue.Phase != EnhancementPhase.NotInstalled)) {
                status = issue; return false;
            }
            if (install && (!driverReady || !filesReady)) {
                if (!driverReady && OtherBackendRunning()) {
                    Set(EnhancementPhase.Conflict, "请先退出其他 VIIPER / DS4Windows 后再安装驱动。"); return false;
                }
                string cache = Path.Combine(Root, "Downloads");
                if (!filesReady) {
                    ResetOwned();
                    var progress = new DownloadProgress(p => Set(EnhancementPhase.Downloading, "正在下载触觉后端…", p));
                    string zip = await EnhancementComponents.DownloadAsync(EnhancementComponents.Backend, cache, progress, token);
                    Set(EnhancementPhase.Downloading, "正在校验并部署触觉后端…", 1);
                    await EnhancementComponents.DeployBackendAsync(zip, ComponentRoot, token);
                }
                if (!driverReady) {
                    var progress = new DownloadProgress(p => Set(EnhancementPhase.Downloading, "正在下载官方增强驱动…", p));
                    string installer = await EnhancementComponents.DownloadAsync(EnhancementComponents.Driver, cache, progress, token);
                    token.ThrowIfCancellationRequested();
                    Set(EnhancementPhase.InstallingDriver, "请确认 Windows 管理员提示，正在安装增强驱动…");
                    using var process = Process.Start(new ProcessStartInfo(installer,
                        $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /TYPE=compact /COMPONENTS=\"main,client\" /TASKS=\"vcredist\" /DIR=\"{Path.GetDirectoryName(DriverExe)}\" /LOG=\"{Path.Combine(Root, "driver-install.log")}\"") {
                        UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = Path.GetDirectoryName(installer)!, ErrorDialog = false
                    }) ?? throw new IOException("无法启动官方增强驱动安装器");
                    // Do not cancel or kill a shared driver installer after it has started.
                    await process.WaitForExitAsync();
                    if (process.ExitCode is not (0 or 3010 or 1641)) throw new IOException($"官方驱动安装未完成（{process.ExitCode}），可重试或查看详情。");
                    File.WriteAllText(RestartMarker, JsonSerializer.Serialize(new { Boot = BootMarker() }));
                    Set(EnhancementPhase.RestartRequired, "组件已安装，请保存工作并重启电脑。重启后选择增强模式即可继续。"); return false;
                }
            }
            token.ThrowIfCancellationRequested();
            await StartOwnedAsync(token);
            if (!hostVerified) {
                Set(EnhancementPhase.Validating, "正在验证虚拟手柄与触觉音频通道…");
                await validateHost(EnhancementComponents.ApiPort, token); hostVerified = true;
            }
            Set(EnhancementPhase.Ready, "增强环境已就绪，可连接手柄后启动游戏。"); return true;
        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            ResetOwned();
            Set(EnhancementPhase.Error, "增强环境准备超时，请检查网络或稍后重试。", detail: "下载或环境检测超时；已下载且通过校验的组件会继续复用。"); return false;
        } catch (OperationCanceledException) {
            ResetOwned();
            Set(EnhancementPhase.Cancelled, "已取消。重新检测后可继续，已完成的组件会保留。"); return false;
        } catch (Win32Exception error) when (error.NativeErrorCode == 1223) {
            Set(EnhancementPhase.Cancelled, "已取消管理员授权，通用模式仍可使用。"); return false;
        } catch (Exception error) {
            ResetOwned();
            Set(EnhancementPhase.Error, "增强环境准备未完成，可重试或使用通用模式。", detail: error.Message);
            AppLog.Write("warn", "增强环境：" + error.Message); return false;
        } finally { operation?.Dispose(); operation = null; gate.Release(); }
    }

    private async Task StartOwnedAsync(CancellationToken token)
    {
        if (backend is not null && !backend.Process.HasExited) { await ViiperGamepad.ProbeAsync(EnhancementComponents.ApiPort, token); return; }
        backend?.Dispose(); backend = null; hostVerified = false;
        if (!await EnhancementComponents.HashMatchesAsync(BackendPath, EnhancementComponents.BackendExeHash, token)) throw new IOException("增强程序校验失败，请重新安装组件。");
        foreach (int port in new[] { EnhancementComponents.ApiPort, EnhancementComponents.UsbPort }) {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try { listener.Start(); } catch (SocketException) { throw new IOException("增强组件专用端口被占用。请关闭占用该端口的软件后重试，或使用高级选项中的已有后端。"); }
            finally { listener.Stop(); }
        }
        Set(EnhancementPhase.Starting, "正在启动触觉后端…");
        string config = Path.Combine(Root, "server.json"); File.WriteAllText(config, "{}");
        var start = new ProcessStartInfo(BackendPath) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Root };
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("VIIPER_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        foreach (string argument in new[] { "--config-only", "--config", config, "--log.file=", "--log.raw-file=", "server", "--key-file", Path.Combine(Root, "backend.key"),
            "--usb.addr=127.0.0.1:" + EnhancementComponents.UsbPort, "--api.addr=127.0.0.1:" + EnhancementComponents.ApiPort,
            "--api.require-local-host-auth=false", "--api.auto-attach-local-client=true", "--api.auto-attach-windows-native=true" }) start.ArgumentList.Add(argument);
        backend = new OwnedBackendProcess(start);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(25)) {
            token.ThrowIfCancellationRequested();
            if (backend.Process.HasExited) throw new IOException($"增强后端启动失败（{backend.Process.ExitCode}）。请重新检测驱动；安装过驱动后需重启电脑。");
            try {
                string identity = await ViiperGamepad.ProbeAsync(EnhancementComponents.ApiPort, token);
                if (!identity.Contains(EnhancementComponents.BackendVersion, StringComparison.Ordinal)) throw new IOException("增强后端版本不匹配");
                return;
            } catch (IOException) { await Task.Delay(200, token); }
        }
        throw new TimeoutException("增强后端启动超时，请重试；如刚安装驱动，请先重启电脑。");
    }

    private static string? ReadInstalledDriverVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{199505b0-b93d-4521-a8c7-897818e0205a}_is1");
        string? registered = key?.GetValue("DisplayVersion") as string;
        if (!string.IsNullOrWhiteSpace(registered)) return registered.Trim();
        if (File.Exists(DriverExe)) return FileVersionInfo.GetVersionInfo(DriverExe).FileVersion?.Trim() ?? "未知版本";
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\usbip2_ude");
        return service is not null ? "未知版本（残留驱动）" : null;
    }
    private static bool OtherBackendRunning()
    {
        var processes = Process.GetProcessesByName("viiper");
        try { return processes.Any(p => !p.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private void ResetOwned() { backend?.Dispose(); backend = null; hostVerified = false; }
    public async Task StopOwnedAsync()
    {
        await gate.WaitAsync();
        try { ResetOwned(); Set(EnhancementPhase.Unchecked, "先检测已有组件，缺少时再提示安装。"); }
        finally { gate.Release(); }
    }
    public async Task WaitForIdleAsync() { await gate.WaitAsync(); gate.Release(); }
    private static bool HasAbiFailure(string text) => new[] { "abi mismatch", "unexpected size", "invalid structure size", "specified conversion is not valid" }.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
    private static async Task<string> RunReadOnlyAsync(string file, string argument, CancellationToken token)
    {
        using var process = Process.Start(new ProcessStartInfo(file, argument) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true }) ?? throw new IOException("无法检查增强驱动");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(output, errors); }
        catch { try { process.Kill(true); } catch (Exception) { } throw; }
        if (process.ExitCode != 0) throw new IOException($"USBIP {argument} 检查失败（退出码 {process.ExitCode}）。\n{await output}\n{await errors}");
        return await output + await errors;
    }
    private static long BootMarker() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Environment.TickCount64;
    private sealed class DownloadProgress(Action<double> changed) : IProgress<double> { public void Report(double value) => changed(value); }
    private static bool AwaitingRestart()
    {
        if (!File.Exists(RestartMarker)) return false;
        try {
            using var json = JsonDocument.Parse(File.ReadAllText(RestartMarker));
            if (Math.Abs(json.RootElement.GetProperty("Boot").GetInt64() - BootMarker()) < 30_000) return true;
        } catch (Exception) { return true; }
        File.Delete(RestartMarker); return false;
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); await gate.WaitAsync();
        try { backend?.Dispose(); backend = null; hostVerified = false; }
        finally { gate.Release(); }
    }
}
