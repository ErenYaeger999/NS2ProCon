using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NS2Pro.App.Services;

public enum UpdateCheckState { Current, Available, Unavailable }
public sealed record UpdateCheckResult(UpdateCheckState State, string? LatestVersion = null);

/// <summary>Read-only public release check; downloads and installation remain user initiated.</summary>
public sealed class UpdateService(HttpClient? client = null)
{
    public const string ReleasesUrl = "https://github.com/ErenYaeger999/NS2ProCon/releases/latest";
    private const string ApiUrl = "https://api.github.com/repos/ErenYaeger999/NS2ProCon/releases/latest";
    private static readonly HttpClient SharedClient = new() {
        Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 256 * 1024
    };

    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken token = default)
    {
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("NS2ProCon/" + Normalize(current).ToString(3));
            using var response = await (client ?? SharedClient).SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(UpdateCheckState.Unavailable);
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Parse(json, current);
        } catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException) {
            return new(UpdateCheckState.Unavailable);
        }
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
        string value = tag.GetString()!;
        // Stable numeric release tags only. A release title or response URL is never executed.
        if (!Regex.IsMatch(value, @"\A[vV]?[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant) ||
            !Version.TryParse(value.TrimStart('v', 'V'), out var latest)) return new(UpdateCheckState.Unavailable);
        latest = Normalize(latest);
        return new(latest > Normalize(current) ? UpdateCheckState.Available : UpdateCheckState.Current,
            latest.ToString(latest.Revision == 0 ? 3 : 4));
    }

    private static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
}
