using System.Net;
using System.Net.Http;
using NS2Pro.App.Services;

internal static class UpdateChecks
{
    internal static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Updates: " + message); passed++; }
        var current = new Version(0, 7, 0, 0);
        static string Release(string tag, bool prerelease = false, bool draft = false) =>
            System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag, prerelease, draft,
                html_url = "https://untrusted.invalid/file.exe", name = "v99.0.0" });
        foreach (var tag in new[] { "v0.7.1", "0.8.0", "V1.0.0", "v0.10.0", "v0.7.0.1" })
            Check(UpdateService.Parse(Release(tag), current).State == UpdateCheckState.Available, "numeric comparison detects " + tag);
        foreach (var tag in new[] { "v0.7.0", "0.7.0.0", "v0.6.1" })
            Check(UpdateService.Parse(Release(tag), current).State == UpdateCheckState.Current, "equal/older release never prompts a downgrade: " + tag);
        foreach (var tag in new[] { "v0.8.0-beta.1", "v0.8", "v999999999999.0.0", "latest", "https://untrusted.invalid" })
            Check(UpdateService.Parse(Release(tag), current).State == UpdateCheckState.Unavailable, "unsupported release tags fail quietly");
        Check(UpdateService.Parse(Release("v2.0.0", prerelease: true), current).State == UpdateCheckState.Unavailable &&
            UpdateService.Parse(Release("v2.0.0", draft: true), current).State == UpdateCheckState.Unavailable, "drafts and prereleases are excluded");
        foreach (var json in new[] { "[]", "null", "{}", "{\"tag_name\":9,\"draft\":false,\"prerelease\":false}" })
            Check(UpdateService.Parse(json, current).State == UpdateCheckState.Unavailable, "invalid API schema fails quietly");
        Check(UpdateService.Parse(Release("v0.8.0"), current).LatestVersion == "0.8.0" &&
            UpdateService.ReleasesUrl == "https://github.com/ErenYaeger999/NS2ProCon/releases/latest", "download link remains fixed to the project release page");

        using var handler = new ReplyHandler();
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);
        handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release("v0.8.0")) });
        Check((await service.CheckAsync(current)).State == UpdateCheckState.Available, "public API result flows through the HTTP check");
        Check(handler.Address == "https://api.github.com/repos/ErenYaeger999/NS2ProCon/releases/latest" &&
            handler.UserAgent == "NS2ProCon/0.7.0" && !handler.HasAuthorization, "anonymous metadata request includes a User-Agent and no credentials");
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway }) {
            handler.Reply = _ => Task.FromResult(new HttpResponseMessage(status));
            Check((await service.CheckAsync(current)).State == UpdateCheckState.Unavailable, "HTTP failure is nonfatal: " + status);
        }
        handler.Reply = _ => throw new HttpRequestException("offline");
        Check((await service.CheckAsync(current)).State == UpdateCheckState.Unavailable, "offline startup cannot fail the app");
        handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not JSON") });
        Check((await service.CheckAsync(current)).State == UpdateCheckState.Unavailable, "malformed network response is nonfatal");
        handler.Reply = async token => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); };
        using var cancel = new CancellationTokenSource(25);
        Check((await service.CheckAsync(current, cancel.Token)).State == UpdateCheckState.Unavailable, "closing during an in-flight request cancels cleanly");
        return passed;
    }

    private sealed class ReplyHandler : HttpMessageHandler
    {
        public Func<CancellationToken, Task<HttpResponseMessage>> Reply = _ => throw new Exception("No reply");
        public string? Address, UserAgent;
        public bool HasAuthorization;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Address = request.RequestUri!.AbsoluteUri; UserAgent = request.Headers.UserAgent.ToString();
            HasAuthorization = request.Headers.Authorization is not null;
            return Reply(cancellationToken);
        }
    }
}
