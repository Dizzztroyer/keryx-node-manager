using System.Text.Json.Serialization;

namespace KeryxNodeManager.Core.Updates;

/// <summary>One downloadable file attached to a GitHub release.</summary>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long SizeBytes);

/// <summary>
/// The subset of a GitHub "latest release" response this app actually needs. `TagName` is used
/// as-is for version comparison (Keryx-Labs' tags are `vMAJOR.MINOR.PATCH-SUFFIX`, e.g.
/// `v1.4.4-OPoI` - not pure semver, so this app treats any tag mismatch against the locally
/// recorded installed version as "an update exists" rather than trying to parse and order
/// versions numerically, which would be over-engineering for a value that's always fetched via
/// `/releases/latest` - GitHub has already done the "which one is latest" work).
/// </summary>
public sealed record LatestReleaseInfo(string TagName, DateTimeOffset? PublishedAt, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>
    /// Finds this release's Windows build. Keryx-Labs' release assets follow
    /// `{repo}-{tag}-win64-amd64.zip` (confirmed live against the real
    /// Keryx-Labs/keryx-node and Keryx-Labs/keryx-miner repos) alongside Linux/HiveOS variants -
    /// matching on "win64" + ".zip" rather than hardcoding the full pattern is deliberately a
    /// little loose, so a future minor renaming (e.g. an added architecture suffix) doesn't
    /// silently break matching the way an exact-string match would.
    /// </summary>
    public ReleaseAsset? FindWindowsAsset() => Assets.FirstOrDefault(a =>
        a.Name.Contains("win64", StringComparison.OrdinalIgnoreCase) &&
        a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Queries the public, unauthenticated GitHub Releases API for a repo's latest release. Used to
/// check for new keryxd/keryx-miner builds (brief-adjacent request: "auto-update the node/miner
/// binaries from their own upstream repos"). Deliberately read-only and unauthenticated - this app
/// has no GitHub token to spend, and the public `/releases/latest` endpoint is exactly the
/// information needed. Unauthenticated GitHub API calls are rate-limited to 60/hour per IP, which
/// is generous for "check on startup + on-demand button click," but callers should not poll this
/// in a tight loop.
/// </summary>
public sealed class GitHubReleaseChecker
{
    private readonly HttpClient _httpClient;

    public GitHubReleaseChecker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LatestReleaseInfo> GetLatestReleaseAsync(string owner, string repo, CancellationToken ct = default)
    {
        // GitHub's API rejects requests with no User-Agent header outright (HTTP 403) - this is
        // not optional decoration, it's a hard requirement of the API itself.
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
        request.Headers.UserAgent.ParseAdd("KeryxNodeManager");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubReleaseCheckException(Localization.CoreStrings.Format(
                "Update.ReleaseCheckFailed", $"{owner}/{repo}", (int)response.StatusCode));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var dto = await System.Text.Json.JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, cancellationToken: ct)
            ?? throw new GitHubReleaseCheckException(Localization.CoreStrings.Format(
                "Update.ReleaseCheckEmptyResponse", $"{owner}/{repo}"));

        var assets = (dto.Assets ?? new List<GitHubAssetDto>())
            .Where(a => a.Name is not null && a.BrowserDownloadUrl is not null)
            .Select(a => new ReleaseAsset(a.Name!, new Uri(a.BrowserDownloadUrl!), a.Size))
            .ToList();

        return new LatestReleaseInfo(dto.TagName ?? "unknown", dto.PublishedAt, assets);
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto>? Assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}

public sealed class GitHubReleaseCheckException : Exception
{
    public GitHubReleaseCheckException(string message) : base(message) { }
}

/// <summary>The two binaries this app knows how to check/update. Both live in the Keryx-Labs
/// GitHub org - see CHANGELOG.md, which is where these repo names were originally confirmed.</summary>
public enum ManagedBinaryKind
{
    Node,
    Miner,
}

public static class KeryxRepos
{
    public const string Owner = "Keryx-Labs";
    public const string NodeRepo = "keryx-node";
    public const string MinerRepo = "keryx-miner";

    public static string RepoNameFor(ManagedBinaryKind kind) => kind switch
    {
        ManagedBinaryKind.Node => NodeRepo,
        ManagedBinaryKind.Miner => MinerRepo,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The exe filename this app expects to find inside the downloaded archive - used to
    /// prefer an exact match over "just grab the first .exe" when a zip contains more than one
    /// (e.g. a bundled helper tool).</summary>
    public static string ExeFileNameFor(ManagedBinaryKind kind) => kind switch
    {
        ManagedBinaryKind.Node => "keryxd.exe",
        ManagedBinaryKind.Miner => "keryx-miner.exe",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
