using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace NS2Pro.App.Services;

public sealed record ComponentPackage(string FileName, string Url, long Size, string Sha256);

/// <summary>Fixed upstream releases; downloaded only after an explicit install action.</summary>
public static class EnhancementComponents
{
    public const string BackendVersion = "0.1.9-rc4.6.6", DriverVersion = "0.9.7.7";
    public const int ApiPort = 32452, UsbPort = 32451;
    public const string BackendSource = "https://github.com/hbashton/VIIPER/releases/tag/v0.1.9-rc4.6.6";
    public const string DriverSource = "https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.7.7";
    public const string BackendExeHash = "9392A49E619E1D9B7956EA4BEDAFD2E74CBF5065C011554E80F756D1524F892B";
    public static readonly ComponentPackage Backend = new("viiper-windows-amd64.zip",
        "https://github.com/hbashton/VIIPER/releases/download/v0.1.9-rc4.6.6/viiper-windows-amd64.zip",
        3957811, "C68D89319A12D4ADC408EE8C779917B943C2AE2FAC247F9933330E25B04C5842");
    // This VIIPER build rejects even newer USBIP versions. Never silently downgrade a shared driver.
    public static readonly ComponentPackage Driver = new("USBip-0.9.7.7-x64.exe",
        "https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.7.7/USBip-0.9.7.7-x64.exe",
        33226344, "51620FA5F9F8BE5932BC9D786DEEE557CE06D5407A99CAB490DCFAC71F185FEA");
    private static readonly HashSet<string> PayloadNames = new(StringComparer.OrdinalIgnoreCase) {
        "viiper.exe", "licenses.txt", "LICENSE.txt", "VIIPER-SYSTRAY-NOTICE.md",
        "VIIPER-SYSTRAY-LICENSE.txt", "VIIPER-WDL-NOTICE.txt", "BUILD-INFO.json"
    };
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<bool> IsBackendReadyAsync(string directory, CancellationToken token) =>
        PayloadNames.All(name => File.Exists(Path.Combine(directory, name))) &&
        await HashMatchesAsync(Path.Combine(directory, "viiper.exe"), BackendExeHash, token);

    public static async Task<bool> HashMatchesAsync(string file, string hash, CancellationToken token = default)
    {
        if (!File.Exists(file)) return false;
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> DownloadAsync(ComponentPackage package, string cache, IProgress<double>? progress,
        CancellationToken token, HttpClient? http = null)
    {
        Directory.CreateDirectory(cache);
        if (Path.GetFileName(package.FileName) != package.FileName) throw new InvalidDataException("组件文件名无效");
        string destination = Path.Combine(cache, package.FileName);
        if (await HashMatchesAsync(destination, package.Sha256, token)) { progress?.Report(1); return destination; }
        string partial = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, package.Url);
            request.Headers.UserAgent.ParseAdd("NS2ProCon/0.8.0");
            using var response = await (http ?? Http).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) {
                byte[] buffer = new byte[65536]; long received = 0; int count;
                while ((count = await source.ReadAsync(buffer, deadline.Token)) > 0) {
                    received += count;
                    if (received > package.Size) throw new InvalidDataException("组件下载大小与固定版本不符，请重试");
                    await target.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                    progress?.Report((double)received / package.Size);
                }
                if (received != package.Size) throw new InvalidDataException("组件下载未完成，请重试");
            }
            if (!await HashMatchesAsync(partial, package.Sha256, deadline.Token)) throw new InvalidDataException("组件校验失败，未执行下载文件。请重试");
            File.Move(partial, destination, true); return destination;
        } finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public static async Task DeployBackendAsync(string archivePath, string componentRoot, CancellationToken token)
    {
        if (!await HashMatchesAsync(archivePath, Backend.Sha256, token)) throw new InvalidDataException("增强组件压缩包校验失败");
        Directory.CreateDirectory(componentRoot);
        string destination = Path.Combine(componentRoot, BackendVersion);
        if (await IsBackendReadyAsync(destination, token)) return;
        string stage = Path.Combine(componentRoot, ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string? replaced = null;
        try {
            using var zip = ZipFile.OpenRead(archivePath);
            ValidateArchive(zip);
            foreach (var entry in zip.Entries) {
                token.ThrowIfCancellationRequested();
                entry.ExtractToFile(Path.Combine(stage, entry.FullName));
            }
            if (!await HashMatchesAsync(Path.Combine(stage, "viiper.exe"), BackendExeHash, token)) throw new InvalidDataException("增强组件程序校验失败");
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(destination)) {
                replaced = Path.Combine(componentRoot, ".replaced-" + Guid.NewGuid().ToString("N"));
                Directory.Move(destination, replaced);
            }
            try { Directory.Move(stage, destination); }
            catch { if (replaced is not null) Directory.Move(replaced, destination); throw; }
        } finally {
            if (Directory.Exists(stage)) DeleteOwnedDirectory(componentRoot, stage);
            if (replaced is not null && Directory.Exists(replaced) && Directory.Exists(destination)) DeleteOwnedDirectory(componentRoot, replaced);
        }
    }

    internal static void ValidateArchive(ZipArchive zip)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var entry in zip.Entries) {
            if (!PayloadNames.Contains(entry.FullName) || !names.Add(entry.FullName) ||
                entry.FullName.Contains('/') || entry.FullName.Contains('\\')) throw new InvalidDataException("增强组件压缩包包含非预期文件");
            size += entry.Length;
            if (size > 20_000_000) throw new InvalidDataException("增强组件解压大小异常");
        }
        if (!names.SetEquals(PayloadNames)) throw new InvalidDataException("增强组件缺少程序或许可证文件");
    }

    private static void DeleteOwnedDirectory(string root, string target)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        target = Path.GetFullPath(target);
        string name = Path.GetFileName(target);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !(name.StartsWith(".stage-", StringComparison.Ordinal) || name.StartsWith(".replaced-", StringComparison.Ordinal)))
            throw new InvalidOperationException("组件临时目录不在允许范围内");
        Directory.Delete(target, true);
    }
}
