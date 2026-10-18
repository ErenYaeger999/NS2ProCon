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
            handler.UserAgent == "NS2ProCon/0.7.0" && !handler.HasAuthorization && handler.PageCalls == 0, "valid anonymous API metadata needs no fallback or credentials");
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway }) {
            handler.Reply = _ => Task.FromResult(new HttpResponseMessage(status));
            Check((await service.CheckAsync(current)).State == UpdateCheckState.Unavailable, "HTTP failure is nonfatal: " + status);
        }
        static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };
        handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Headers = { { "X-RateLimit-Remaining", "0" } } });
        handler.FallbackReply = _ => Task.FromResult(Redirect("/ErenYaeger999/NS2ProCon/releases/tag/v0.8.5"));
        var fallback = await service.CheckAsync(current);
        Check(fallback.State == UpdateCheckState.Available && fallback.LatestVersion == "0.8.5" && fallback.Detail?.Contains("限流") == true,
            "anonymous rate limiting falls back to the official release redirect with a diagnostic reason");
        Check(handler.Address == UpdateService.ReleasesUrl && handler.Method == HttpMethod.Head && !handler.HasAuthorization,
            "fallback reads public version headers only, without credentials or installer downloads");
        handler.FallbackReply = _ => Task.FromResult(Redirect("https://github.com/ErenYaeger999/NS2ProCon/releases/tag/v0.8.5"));
        Check((await service.CheckAsync(new Version(0, 8, 5))).State == UpdateCheckState.Current,
            "absolute release redirects correctly identify the installed version");
        handler.Reply = _ => throw new HttpRequestException("API unavailable");
        Check((await service.CheckAsync(current)).State == UpdateCheckState.Available,
            "an unavailable API does not block a reachable official release page");
        handler.Reply = _ => throw new TaskCanceledException();
        Check((await service.CheckAsync(current)).State == UpdateCheckState.Available,
            "an API-specific timeout leaves the fallback an independent opportunity");
        handler.FallbackReply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            RequestMessage = new(HttpMethod.Head, "https://github.com/ErenYaeger999/NS2ProCon/releases/tag/v0.8.5") });
        Check((await service.CheckAsync(current)).State == UpdateCheckState.Available,
            "injected redirect-following clients validate the final response address");
        foreach (string destination in new[] {
            "https://untrusted.invalid/ErenYaeger999/NS2ProCon/releases/tag/v9.0.0",
            "http://github.com/ErenYaeger999/NS2ProCon/releases/tag/v9.0.0",
            "https://github.com/another/project/releases/tag/v9.0.0",
            "https://github.com/ErenYaeger999/NS2ProCon/releases/tag/v9.0.0-beta",
            "https://github.com/ErenYaeger999/NS2ProCon/releases/tag/v9.0.0?redirect=1",
            "https://github.com/ErenYaeger999/NS2ProCon/releases/tag/v9.0.0#fragment",
            "https://user@github.com/ErenYaeger999/NS2ProCon/releases/tag/v9.0.0",
            "https://github.com:444/ErenYaeger999/NS2ProCon/releases/tag/v9.0.0",
            "https://github.com/ErenYaeger999/NS2ProCon/releases/tag/%2Fv9.0.0" }) {
            handler.FallbackReply = _ => Task.FromResult(Redirect(destination));
            Check((await service.CheckAsync(current)).State == UpdateCheckState.Unavailable,
                "fallback rejects a foreign, insecure or unsupported release destination: " + destination);
        }
        handler.FallbackReply = null;
        handler.Reply = _ => throw new HttpRequestException("offline");
        var offline = await service.CheckAsync(current);
        Check(offline.State == UpdateCheckState.Unavailable && offline.Detail?.Contains("Release 入口") == true, "offline startup is nonfatal and retains both failure reasons");
        handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not JSON") });
        Check((await service.CheckAsync(current)).State == UpdateCheckState.Unavailable, "malformed network response is nonfatal");
        handler.Reply = async token => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); };
        using var cancel = new CancellationTokenSource(25);
        int pagesBeforeCancellation = handler.PageCalls;
        Check((await service.CheckAsync(current, cancel.Token)).State == UpdateCheckState.Unavailable && handler.PageCalls == pagesBeforeCancellation,
            "closing cancels cleanly without starting another request");
        using var limitedHandler = new ReplyHandler();
        using var limitedClient = new HttpClient(limitedHandler);
        var limitedService = new UpdateService(limitedClient);
        int apiCalls = 0;
        limitedHandler.Reply = _ => {
            apiCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Headers = {
                { "X-RateLimit-Remaining", "0" }, { "X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds().ToString() } } });
        };
        limitedHandler.FallbackReply = _ => Task.FromResult(Redirect("/ErenYaeger999/NS2ProCon/releases/tag/v0.8.5"));
        await limitedService.CheckAsync(current);
        Check((await limitedService.CheckAsync(current)).State == UpdateCheckState.Available && apiCalls == 1 && limitedHandler.PageCalls == 2,
            "manual retries honor the API reset time while continuing to check the official release page");
        using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
        int existingRequests = apiCalls + limitedHandler.PageCalls;
        Check((await limitedService.CheckAsync(current, alreadyCancelled.Token)).State == UpdateCheckState.Unavailable &&
            apiCalls + limitedHandler.PageCalls == existingRequests, "cancellation during a rate-limit waiting period sends no network requests");
        var throttledService = new UpdateService(limitedClient);
        int previousApiCalls = apiCalls;
        limitedHandler.Reply = _ => {
            apiCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = {
                RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1)) } });
        };
        await throttledService.CheckAsync(current); await throttledService.CheckAsync(current);
        Check(apiCalls == previousApiCalls + 1, "secondary rate limits honor the Retry-After header too");
        using var stalledHandler = new ReplyHandler();
        using var stalledClient = new HttpClient(stalledHandler);
        stalledHandler.Reply = async ct => { await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); };
        stalledHandler.FallbackReply = _ => Task.FromResult(Redirect("/ErenYaeger999/NS2ProCon/releases/tag/v0.8.5"));
        Check((await new UpdateService(stalledClient).CheckAsync(current).WaitAsync(TimeSpan.FromSeconds(15))).State == UpdateCheckState.Available,
            "a genuinely stalled API expires its own budget without cancelling the release-page fallback");
        return passed;
    }

    private sealed class ReplyHandler : HttpMessageHandler
    {
        public Func<CancellationToken, Task<HttpResponseMessage>> Reply = _ => throw new Exception("No reply");
        public Func<CancellationToken, Task<HttpResponseMessage>>? FallbackReply;
        public string? Address, UserAgent;
        public HttpMethod? Method;
        public int PageCalls;
        public bool HasAuthorization;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Address = request.RequestUri!.AbsoluteUri; UserAgent = request.Headers.UserAgent.ToString();
            Method = request.Method;
            HasAuthorization = request.Headers.Authorization is not null;
            if (request.Method == HttpMethod.Head) { PageCalls++; return (FallbackReply ?? Reply)(cancellationToken); }
            return Reply(cancellationToken);
        }
    }
}
