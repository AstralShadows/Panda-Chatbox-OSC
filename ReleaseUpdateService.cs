using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PandaChatbox;

internal sealed record LatestRelease(Version? Version, Uri PageUri, bool IsPrerelease);
internal sealed record VersionRelease(Version Version, Uri PageUri, string Notes);

internal static class ReleaseUpdateService
{
    const string LatestReleaseApi = "https://api.github.com/repos/AstralShadows/Panda-Chatbox-OSC/releases/latest";

    static readonly HttpClient Http = CreateClient();
    static readonly Regex VersionPattern = new(
        @"(?<!\d)[vV]?(?<version>\d+\.\d+(?:\.\d+){0,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "PandaChatbox",
            typeof(ReleaseUpdateService).Assembly.GetName().Version?.ToString(3) ?? "3.5.2"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<LatestRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(LatestReleaseApi, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        var release = await ReadReleaseAsync(response, cancellationToken);
        return new LatestRelease(release.Version, release.PageUri, release.IsPrerelease);
    }

    public static async Task<VersionRelease?> GetReleaseForVersionAsync(Version version, CancellationToken cancellationToken)
    {
        string tag = Uri.EscapeDataString(version.ToString());
        using var response = await Http.GetAsync(
            $"https://api.github.com/repos/AstralShadows/Panda-Chatbox-OSC/releases/tags/{tag}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        var release = await ReadReleaseAsync(response, cancellationToken);
        if (release.Version != version)
            throw new JsonException($"GitHub returned release version {release.Version?.ToString() ?? "unknown"} instead of {version}.");
        return new VersionRelease(version, release.PageUri, release.Notes);
    }

    static async Task<GitHubReleaseInfo> ReadReleaseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken)
            ?? throw new JsonException("GitHub returned an empty latest-release response.");

        if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var pageUri)
            || pageUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(pageUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !pageUri.AbsolutePath.StartsWith("/AstralShadows/Panda-Chatbox-OSC/releases/", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("GitHub returned an invalid release page URL.");
        }

        Version? version = ParseVersion(release.TagName) ?? ParseVersion(release.Name);

        return new GitHubReleaseInfo(version, pageUri, release.Prerelease, release.Body);
    }

    static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = VersionPattern.Match(text);
        return match.Success && Version.TryParse(match.Groups["version"].Value, out var version)
            ? version
            : null;
    }

    sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = "";

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("body")]
        public string Body { get; init; } = "";
    }

    sealed record GitHubReleaseInfo(Version? Version, Uri PageUri, bool IsPrerelease, string Notes);
}
