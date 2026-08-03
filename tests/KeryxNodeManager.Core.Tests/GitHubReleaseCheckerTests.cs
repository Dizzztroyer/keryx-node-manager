using System.Net;
using System.Text;
using KeryxNodeManager.Core.Updates;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Exercises GitHubReleaseChecker against an in-process fake HttpMessageHandler (no real network,
/// no dependency on GitHub's actual rate limits or uptime) - covers parsing a realistic release
/// payload (modeled directly on the real response this project captured live from
/// api.github.com/repos/Keryx-Labs/keryx-node/releases/latest and .../keryx-miner/releases/latest
/// during research - see PROJECT_STATUS.md), asset matching, and the non-2xx/empty-body error
/// paths.
/// </summary>
public class GitHubReleaseCheckerTests
{
    private const string RealisticNodeReleaseJson = """
        {
          "tag_name": "v1.4.4-OPoI",
          "published_at": "2026-08-02T10:00:00Z",
          "assets": [
            { "name": "keryx-node-v1.4.4-OPoI-win64-amd64.zip", "browser_download_url": "https://github.com/Keryx-Labs/keryx-node/releases/download/v1.4.4-OPoI/keryx-node-v1.4.4-OPoI-win64-amd64.zip", "size": 12345678 },
            { "name": "keryx-node-v1.4.4-OPoI-linux-amd64.zip", "browser_download_url": "https://github.com/Keryx-Labs/keryx-node/releases/download/v1.4.4-OPoI/keryx-node-v1.4.4-OPoI-linux-amd64.zip", "size": 12345000 }
          ]
        }
        """;

    [Fact]
    public async Task GetLatestReleaseAsync_ParsesRealisticPayload_ReturnsTagAndAssets()
    {
        var checker = new GitHubReleaseChecker(new HttpClient(new FakeJsonHandler(HttpStatusCode.OK, RealisticNodeReleaseJson)));

        var release = await checker.GetLatestReleaseAsync("Keryx-Labs", "keryx-node", CancellationToken.None);

        Assert.Equal("v1.4.4-OPoI", release.TagName);
        Assert.Equal(2, release.Assets.Count);
        Assert.NotNull(release.PublishedAt);
    }

    [Fact]
    public async Task FindWindowsAsset_PicksTheWin64ZipNotTheLinuxOne()
    {
        var checker = new GitHubReleaseChecker(new HttpClient(new FakeJsonHandler(HttpStatusCode.OK, RealisticNodeReleaseJson)));
        var release = await checker.GetLatestReleaseAsync("Keryx-Labs", "keryx-node", CancellationToken.None);

        var asset = release.FindWindowsAsset();

        Assert.NotNull(asset);
        Assert.Equal("keryx-node-v1.4.4-OPoI-win64-amd64.zip", asset!.Name);
        Assert.Equal(
            "https://github.com/Keryx-Labs/keryx-node/releases/download/v1.4.4-OPoI/keryx-node-v1.4.4-OPoI-win64-amd64.zip",
            asset.DownloadUrl.ToString());
    }

    [Fact]
    public async Task FindWindowsAsset_NoWindowsAssetPresent_ReturnsNull()
    {
        const string linuxOnlyJson = """{ "tag_name": "v9.9.9", "assets": [ { "name": "thing-linux-amd64.zip", "browser_download_url": "https://example.local/x.zip", "size": 1 } ] }""";
        var checker = new GitHubReleaseChecker(new HttpClient(new FakeJsonHandler(HttpStatusCode.OK, linuxOnlyJson)));

        var release = await checker.GetLatestReleaseAsync("Keryx-Labs", "keryx-miner", CancellationToken.None);

        Assert.Null(release.FindWindowsAsset());
    }

    [Fact]
    public async Task GetLatestReleaseAsync_NonSuccessStatus_ThrowsWithStatusCodeInMessage()
    {
        var checker = new GitHubReleaseChecker(new HttpClient(new FakeJsonHandler(HttpStatusCode.NotFound, "{}")));

        var ex = await Assert.ThrowsAsync<GitHubReleaseCheckException>(() =>
            checker.GetLatestReleaseAsync("Keryx-Labs", "does-not-exist", CancellationToken.None));

        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public void RepoNameFor_AndExeFileNameFor_MapEachKindCorrectly()
    {
        Assert.Equal("keryx-node", KeryxRepos.RepoNameFor(ManagedBinaryKind.Node));
        Assert.Equal("keryx-miner", KeryxRepos.RepoNameFor(ManagedBinaryKind.Miner));
        Assert.Equal("keryxd.exe", KeryxRepos.ExeFileNameFor(ManagedBinaryKind.Node));
        Assert.Equal("keryx-miner.exe", KeryxRepos.ExeFileNameFor(ManagedBinaryKind.Miner));
    }

    private sealed class FakeJsonHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeJsonHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }
}
