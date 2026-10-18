using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NS2Pro.App.Services;

public enum UpdateCheckState { Current, Available, Unavailable }
public sealed record UpdateCheckResult(UpdateCheckState State, string? LatestVersion = null, string? Detail = null);

/// <summary>Read-only public release check; downloads and installation remain user initiated.</summary>
public sealed class UpdateService(HttpClient? client = null)
{
    public const string ReleasesUrl = "https://github.com/ErenYaeger999/NS2ProCon/releases/latest";
    private const string ApiUrl = "https://api.github.com/repos/ErenYaeger999/NS2ProCon/releases/latest";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false }) {
        Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 256 * 1024
    };
    private long apiRetryAfter;

    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string issue = "GitHub API 仍在限流等待期";
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= Interlocked.Read(ref apiRetryAfter)) {
            try {
                using var apiTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                apiTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.UserAgent.ParseAdd("NS2ProCon/" + Normalize(current).ToString(3));
                using var response = await (client ?? SharedClient).SendAsync(request, apiTimeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) {
                    var parsed = Parse(await response.Content.ReadAsStringAsync(apiTimeout.Token).ConfigureAwait(false), current);
                    if (parsed.State != UpdateCheckState.Unavailable) return parsed;
                    issue = "GitHub API 版本信息无效";
                } else {
                    bool limited = response.StatusCode == HttpStatusCode.TooManyRequests ||
                        (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"));
                    issue = $"GitHub API {(limited ? "匿名请求已限流，" : "")}HTTP {(int)response.StatusCode}";
                    if (limited) RememberRateLimit(response);
                }
            } catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException) {
                issue = Failure("GitHub API", error);
            }
        }
        if (timeout.IsCancellationRequested) return new(UpdateCheckState.Unavailable, Detail: token.IsCancellationRequested ? "检查已取消" : issue);

        // The official /latest redirect is public release metadata too. HEAD neither downloads
        // an installer nor consumes the anonymous REST API quota. No credentials are shipped.
        try {
            using var request = new HttpRequestMessage(HttpMethod.Head, ReleasesUrl);
            request.Headers.UserAgent.ParseAdd("NS2ProCon/" + Normalize(current).ToString(3));
            using var response = await (client ?? SharedClient).SendAsync(request, timeout.Token).ConfigureAwait(false);
            Uri? destination = null;
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) {
                if (response.Headers.Location is { } location) destination = new Uri(new Uri(ReleasesUrl), location);
            } else if (response.IsSuccessStatusCode) destination = response.RequestMessage?.RequestUri;
            var parsed = ParseReleaseLocation(destination, current);
            if (parsed.State != UpdateCheckState.Unavailable)
                return parsed with { Detail = issue + "；已通过官方 Release 入口完成检查" };
            return new(UpdateCheckState.Unavailable, Detail: issue + $"；Release 入口 HTTP {(int)response.StatusCode}，未返回有效正式版地址");
        } catch (Exception error) when (error is HttpRequestException or OperationCanceledException or UriFormatException) {
            return new(UpdateCheckState.Unavailable, Detail: issue + "；" + Failure("Release 入口", error));
        }
    }

    private void RememberRateLimit(HttpResponseMessage response)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? retry = response.Headers.RetryAfter?.Date;
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            retry = now + TimeSpan.FromMilliseconds(Math.Min(delta.TotalMilliseconds, TimeSpan.FromDays(1).TotalMilliseconds));
        if (retry is null && response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) &&
            long.TryParse(resets.FirstOrDefault(), out var seconds) && seconds > now.ToUnixTimeSeconds() && seconds < now.AddDays(1).ToUnixTimeSeconds())
            retry = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (retry > now) Interlocked.Exchange(ref apiRetryAfter, Math.Min(retry.Value.ToUnixTimeMilliseconds(), now.AddDays(1).ToUnixTimeMilliseconds()));
    }

    private static string Failure(string source, Exception error) => source + (error switch {
        OperationCanceledException => "请求已取消或超时",
        JsonException => "返回的 JSON 无效",
        HttpRequestException network => "网络请求失败（" + network.HttpRequestError + "）",
        _ => "返回地址无效"
    });

    internal static UpdateCheckResult ParseReleaseLocation(Uri? location, Version current)
    {
        const string prefix = "/ErenYaeger999/NS2ProCon/releases/tag/";
        if (location is null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps ||
            !location.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !location.IsDefaultPort ||
            location.UserInfo.Length != 0 || location.Query.Length != 0 || location.Fragment.Length != 0 ||
            !location.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return new(UpdateCheckState.Unavailable);
        return Compare(location.AbsolutePath[prefix.Length..], current);
    }

    internal static UpdateCheckResult Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
            return new(UpdateCheckState.Unavailable);
        return Compare(tag.GetString()!, current);
    }

    private static UpdateCheckResult Compare(string value, Version current)
    {
        // Stable numeric release tags only. A release title or response URL is never executed.
        if (!Regex.IsMatch(value, @"\A[vV]?[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant) ||
            !Version.TryParse(value.TrimStart('v', 'V'), out var latest)) return new(UpdateCheckState.Unavailable);
        latest = Normalize(latest);
        return new(latest > Normalize(current) ? UpdateCheckState.Available : UpdateCheckState.Current,
            latest.ToString(latest.Revision == 0 ? 3 : 4));
    }

    private static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
}
