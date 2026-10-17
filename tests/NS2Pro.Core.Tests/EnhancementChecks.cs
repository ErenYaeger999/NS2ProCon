using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using NS2Pro.App.Services;

internal static class EnhancementChecks
{
    internal static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Enhancement: " + message); passed++; }
        var old = AppSettings.FromJson("{\"OutputMode\":1,\"ViiperPort\":4567}");
        Check(!old.ManageHapticBackend && old.ViiperPort == 4567, "existing external backend settings survive upgrade");
        Check(AppSettings.FromJson("{}").ManageHapticBackend && new AppSettings().OutputMode == GameOutputMode.Xbox,
            "new installs keep Xbox as the default and opt into managed components only on demand");
        Check(AppSettings.FromJson("{\"OutputMode\":1,\"ManageHapticBackend\":true}").ManageHapticBackend,
            "explicit managed selection is not migrated to external ownership");
        Check(AppSettings.FromJson("null").OutputMode == GameOutputMode.Xbox, "null settings recover without property lookup failure");
        var pending = new AppSettings { PreferNativeHaptics = true };
        Check(AppSettings.FromJson(System.Text.Json.JsonSerializer.Serialize(pending)).PreferNativeHaptics && pending.OutputMode == GameOutputMode.Xbox,
            "pending enhancement selection survives reboot while the active output remains Xbox until validated");
        Check(EnhancementComponents.DriverVersion == "0.9.7.7" && EnhancementComponents.Driver.Url.Contains("/v.0.9.7.7/"),
            "driver is pinned to the exact ABI version required by this backend");
        Check(EnhancementComponents.ApiPort != 3242 && EnhancementComponents.UsbPort != 3241,
            "owned backend does not take another client's default ports");
        var uncheckedState = new EnhancementStatus(EnhancementPhase.Unchecked, "");
        Check(uncheckedState.NextAction(true) == EnhancementAction.Check && uncheckedState.NextAction(false) == EnhancementAction.Check,
            "an unchecked environment requires detection, never installation");
        Check(EnhancementStatus.PrerequisiteIssue(EnhancementComponents.DriverVersion, true, true) is null,
            "healthy installed components are reused without an installation step");
        var missingBackend = EnhancementStatus.PrerequisiteIssue(EnhancementComponents.DriverVersion, true, false)!;
        Check(missingBackend.Missing == MissingEnhancementComponents.Backend && missingBackend.NextAction(true) == EnhancementAction.Install &&
            missingBackend.DownloadMegabytes == 4 && !missingBackend.ActionText(true).Contains("驱动"),
            "missing backend offers only the small component download, reusing the installed driver");
        var missingDriver = EnhancementStatus.PrerequisiteIssue(null, false, true)!;
        Check(missingDriver.Missing == MissingEnhancementComponents.Driver && missingDriver.DownloadMegabytes == 33,
            "missing driver preserves an existing backend and shows only its download size");
        var missingBoth = EnhancementStatus.PrerequisiteIssue(null, false, false)!;
        Check(missingBoth.Missing == (MissingEnhancementComponents.Backend | MissingEnhancementComponents.Driver) && missingBoth.DownloadMegabytes == 37,
            "a fresh system explicitly offers both missing components");
        var unhealthy = EnhancementStatus.PrerequisiteIssue(EnhancementComponents.DriverVersion, false, false, "ABI check failed")!;
        Check(unhealthy.Phase == EnhancementPhase.Error && unhealthy.NextAction(true) == EnhancementAction.Check && unhealthy.Detail.Contains("ABI check failed"),
            "installed but unhealthy driver produces a diagnostic error instead of repeated installation");
        var conflict = EnhancementStatus.PrerequisiteIssue("0.9.8.1", false, false)!;
        Check(conflict.Phase == EnhancementPhase.Conflict && conflict.NextAction(true) == EnhancementAction.Check,
            "incompatible shared driver cannot be silently reinstalled or downgraded");
        foreach (var phase in new[] { EnhancementPhase.Error, EnhancementPhase.Cancelled, EnhancementPhase.RestartRequired, EnhancementPhase.Conflict }) {
            var recovery = new EnhancementStatus(phase, "") { Missing = MissingEnhancementComponents.Driver };
            Check(recovery.NextAction(true) == EnhancementAction.Check && recovery.ActionText(true) == "重新检测",
                "recovery actions detect without installing even if old missing-component flags remain: " + phase);
        }
        var externalFailure = new EnhancementStatus(EnhancementPhase.Error, "");
        Check(externalFailure.NextAction(false) == EnhancementAction.UseManaged && externalFailure.ActionText(false).Contains("检测"),
            "external failure explicitly offers switching ownership and detection, not installation");
        Check(new EnhancementStatus(EnhancementPhase.Ready, "").NextAction(false) == EnhancementAction.Connect,
            "a validated external backend can connect without switching ownership");
        string root = Path.Combine(Path.GetTempPath(), "NS2Pro-enhancement-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            byte[] payload = "verified payload"u8.ToArray();
            var package = new ComponentPackage("component.zip", "https://example.invalid/component", payload.Length, Convert.ToHexString(SHA256.HashData(payload)));
            using var handler = new ReplyHandler(); using var client = new HttpClient(handler);
            handler.Reply = token => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
            string file = await EnhancementComponents.DownloadAsync(package, root, null, default, client);
            Check(File.ReadAllBytes(file).SequenceEqual(payload) && handler.Calls == 1, "complete verified download is committed");
            Check(await EnhancementComponents.DownloadAsync(package, root, null, default, client) == file && handler.Calls == 1,
                "verified cache avoids a network request");
            foreach (var bad in new[] { payload[..^1], payload.Concat(new byte[] { 0 }).ToArray(), Enumerable.Repeat((byte)1, payload.Length).ToArray() }) {
                File.WriteAllText(file, "old invalid cache");
                handler.Reply = token => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bad) });
                bool rejected = false;
                try { await EnhancementComponents.DownloadAsync(package, root, null, default, client); } catch (InvalidDataException) { rejected = true; }
                Check(rejected && File.ReadAllText(file) == "old invalid cache" && Directory.GetFiles(root, "*.part").Length == 0,
                    "short, oversized or corrupt downloads never replace the cache or leave a runnable partial");
            }
            handler.Reply = async token => { await Task.Delay(Timeout.Infinite, token); throw new Exception("unreachable"); };
            using (var cancel = new CancellationTokenSource(30)) {
                bool cancelled = false;
                try { await EnhancementComponents.DownloadAsync(package, root, null, cancel.Token, client); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && Directory.GetFiles(root, "*.part").Length == 0, "cancelling a download cleans only its temporary file");
            }
            handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            bool denied = false;
            try { await EnhancementComponents.DownloadAsync(package, root, null, default, client); } catch (HttpRequestException) { denied = true; }
            Check(denied && Directory.GetFiles(root, "*.part").Length == 0, "HTTP failure cannot produce a deployed component");

            string[] names = ["viiper.exe", "licenses.txt", "LICENSE.txt", "VIIPER-SYSTRAY-NOTICE.md", "VIIPER-SYSTRAY-LICENSE.txt", "VIIPER-WDL-NOTICE.txt", "BUILD-INFO.json"];
            bool Validate(IEnumerable<string> entries) {
                using var memory = new MemoryStream();
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (string name in entries) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("test"); }
                memory.Position = 0; using var read = new ZipArchive(memory, ZipArchiveMode.Read);
                try { EnhancementComponents.ValidateArchive(read); return true; } catch (InvalidDataException) { return false; }
            }
            Check(Validate(names), "expected payload retains every third party license");
            foreach (var invalid in new[] { names.Skip(1), names.Where(n => n != "LICENSE.txt"), names.Append("../escape.exe"),
                names.Append("C:\\escape.exe"), names.Append("viiper.exe"), names.Append("unexpected.dll") })
                Check(!Validate(invalid), "incomplete, extra, duplicate and traversing archive entries are rejected");
            bool deploymentRejected = false;
            try { await EnhancementComponents.DeployBackendAsync(file, Path.Combine(root, "backend"), default); }
            catch (InvalidDataException) { deploymentRejected = true; }
            Check(deploymentRejected && !Directory.Exists(Path.Combine(root, "backend")), "wrong package hash is rejected before extraction");
            string[] args = Environment.GetCommandLineArgs();
            int archiveIndex = Array.IndexOf(args, "--component-archive");
            if (archiveIndex >= 0) {
                string zip = Path.GetFullPath(args[archiveIndex + 1]), components = Path.Combine(root, "backend");
                await EnhancementComponents.DeployBackendAsync(zip, components, default);
                string deployed = Path.Combine(components, EnhancementComponents.BackendVersion);
                Check(await EnhancementComponents.IsBackendReadyAsync(deployed, default), "actual pinned upstream archive deploys with all required license files");
                File.Delete(Path.Combine(deployed, "LICENSE.txt"));
                Check(!await EnhancementComponents.IsBackendReadyAsync(deployed, default), "missing license files require repair even if the executable is valid");
                await EnhancementComponents.DeployBackendAsync(zip, components, default);
                Check(await EnhancementComponents.IsBackendReadyAsync(deployed, default) && Directory.GetDirectories(components).Length == 1,
                    "actual package repairs incomplete deployment and removes only its own staging/backup directories");
            }
        } finally {
            string target = Path.GetFullPath(root);
            if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("NS2Pro-enhancement-test-")) throw new Exception("Unexpected cleanup directory");
            Directory.Delete(target, true);
        }
        return passed;
    }
    private sealed class ReplyHandler : HttpMessageHandler
    {
        public int Calls;
        public Func<CancellationToken, Task<HttpResponseMessage>> Reply = _ => throw new Exception("No reply");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return Reply(token); }
    }
}
