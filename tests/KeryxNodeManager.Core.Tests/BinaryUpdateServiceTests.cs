using System.IO.Compression;
using System.Net;
using System.Text;
using KeryxNodeManager.Core.ModelsManagement;
using KeryxNodeManager.Core.Updates;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Exercises BinaryUpdateService's version-comparison, download+extract, and apply-with-backup
/// logic against a real (in-memory-built, temp-dir-written) zip file and a fake HTTP handler - no
/// real network, no real GitHub call. CheckAsync's "is there an update" logic and ApplyUpdate's
/// backup-before-overwrite behavior are the two things a bug here would most dangerously affect
/// (silently not offering a real update, or destroying a working binary with no way back), so both
/// get direct coverage.
/// </summary>
public class BinaryUpdateServiceTests
{
    private const string ReleaseJsonTemplate = """
        { "tag_name": "__TAG__", "assets": [ { "name": "keryxd-win64-amd64.zip", "browser_download_url": "http://fake.local/keryxd.zip", "size": 1 } ] }
        """;

    private static string ReleaseJson(string tag) => ReleaseJsonTemplate.Replace("__TAG__", tag);

    [Fact]
    public async Task CheckAsync_InstalledVersionMatchesLatest_NoUpdateAvailable()
    {
        var service = BuildService(ReleaseJson("v1.4.4-OPoI"));

        var result = await service.CheckAsync(ManagedBinaryKind.Node, "v1.4.4-OPoI", CancellationToken.None);

        Assert.False(result.UpdateAvailable);
        Assert.Equal("v1.4.4-OPoI", result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_InstalledVersionOlder_UpdateAvailable()
    {
        var service = BuildService(ReleaseJson("v1.4.4-OPoI"));

        var result = await service.CheckAsync(ManagedBinaryKind.Node, "v1.4.3-OPoI", CancellationToken.None);

        Assert.True(result.UpdateAvailable);
        Assert.NotNull(result.DownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_NoInstalledVersionRecorded_TreatsAnyLatestAsAnUpdate()
    {
        // Simulates a user who pointed NodeExecutablePath at a manually-installed keryxd.exe
        // before ever using this app's updater - installedVersion is null, not "v0.0.0".
        var service = BuildService(ReleaseJson("v1.4.4-OPoI"));

        var result = await service.CheckAsync(ManagedBinaryKind.Node, installedVersion: null, CancellationToken.None);

        Assert.True(result.UpdateAvailable);
    }

    [Fact]
    public async Task DownloadAndExtractAsync_FindsExactlyNamedExeInsideZip()
    {
        var dir = CreateTempDir();
        try
        {
            var zipBytes = BuildZipWithSingleFile("keryxd.exe", new byte[] { 1, 2, 3, 4 });
            var downloader = new ModelDownloader(new HttpClient(new FakeGetHandler(zipBytes)));
            var service = new BinaryUpdateService(
                new Updates.GitHubReleaseChecker(new HttpClient(new FakeGetHandler(zipBytes))), downloader);

            var exePath = await service.DownloadAndExtractAsync(
                ManagedBinaryKind.Node, new Uri("http://fake.local/keryxd.zip"), dir, progress: null, CancellationToken.None);

            Assert.True(File.Exists(exePath));
            Assert.Equal("keryxd.exe", Path.GetFileName(exePath));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(exePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndExtractAsync_NoMatchingExeInZip_ThrowsBinaryUpdateException()
    {
        var dir = CreateTempDir();
        try
        {
            // Zip contains only a readme, not keryxd.exe and not any single unambiguous .exe.
            var zipBytes = BuildZipWithSingleFile("README.txt", Encoding.UTF8.GetBytes("hello"));
            var downloader = new ModelDownloader(new HttpClient(new FakeGetHandler(zipBytes)));
            var service = new BinaryUpdateService(
                new Updates.GitHubReleaseChecker(new HttpClient(new FakeGetHandler(zipBytes))), downloader);

            await Assert.ThrowsAsync<BinaryUpdateException>(() =>
                service.DownloadAndExtractAsync(
                    ManagedBinaryKind.Node, new Uri("http://fake.local/keryxd.zip"), dir, progress: null, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdate_ExistingTarget_BacksUpBeforeOverwriting()
    {
        var dir = CreateTempDir();
        try
        {
            var extractedPath = Path.Combine(dir, "new_keryxd.exe");
            var targetPath = Path.Combine(dir, "keryxd.exe");
            File.WriteAllBytes(extractedPath, new byte[] { 9, 9, 9 });
            File.WriteAllBytes(targetPath, new byte[] { 1, 1, 1 }); // "old" binary already installed

            var service = new BinaryUpdateService(
                new Updates.GitHubReleaseChecker(new HttpClient(new FakeGetHandler(Array.Empty<byte>()))),
                new ModelDownloader(new HttpClient(new FakeGetHandler(Array.Empty<byte>()))));

            service.ApplyUpdate(extractedPath, targetPath);

            Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(targetPath));
            Assert.True(File.Exists(targetPath + ".bak"));
            Assert.Equal(new byte[] { 1, 1, 1 }, File.ReadAllBytes(targetPath + ".bak")); // old binary recoverable
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdate_CopiesSiblingPluginFilesAlongsideExe()
    {
        // Regression test for the real bug this session found: a release archive commonly bundles
        // plugin/runtime DLLs (e.g. keryx-miner's GPU backend + LLM inference engine) next to the
        // exe, and those files MUST land next to the installed exe too, or a real user hitting
        // "Install update" through this app ends up with a newer exe paired with missing plugins
        // and mining silently breaks ("No workers found") with no error surfaced here at all.
        var extractedDir = CreateTempDir();
        var targetDir = CreateTempDir();
        try
        {
            var extractedExePath = Path.Combine(extractedDir, "keryx-miner.exe");
            File.WriteAllBytes(extractedExePath, new byte[] { 9, 9, 9 });
            File.WriteAllBytes(Path.Combine(extractedDir, "keryxcuda.dll"), new byte[] { 5, 5 });
            File.WriteAllBytes(Path.Combine(extractedDir, "keryx-llama.dll"), new byte[] { 7, 7, 7, 7 });

            var targetExePath = Path.Combine(targetDir, "keryx-miner.exe");
            var service = new BinaryUpdateService(
                new Updates.GitHubReleaseChecker(new HttpClient(new FakeGetHandler(Array.Empty<byte>()))),
                new ModelDownloader(new HttpClient(new FakeGetHandler(Array.Empty<byte>()))));

            service.ApplyUpdate(extractedExePath, targetExePath);

            Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(targetExePath));
            Assert.True(File.Exists(Path.Combine(targetDir, "keryxcuda.dll")));
            Assert.True(File.Exists(Path.Combine(targetDir, "keryx-llama.dll")));
            Assert.Equal(new byte[] { 5, 5 }, File.ReadAllBytes(Path.Combine(targetDir, "keryxcuda.dll")));
            Assert.Equal(new byte[] { 7, 7, 7, 7 }, File.ReadAllBytes(Path.Combine(targetDir, "keryx-llama.dll")));
        }
        finally
        {
            Directory.Delete(extractedDir, recursive: true);
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdate_ExtractedFileMissing_ThrowsWithoutTouchingTarget()
    {
        var dir = CreateTempDir();
        try
        {
            var targetPath = Path.Combine(dir, "keryxd.exe");
            File.WriteAllBytes(targetPath, new byte[] { 1, 1, 1 });
            var service = new BinaryUpdateService(
                new Updates.GitHubReleaseChecker(new HttpClient(new FakeGetHandler(Array.Empty<byte>()))),
                new ModelDownloader(new HttpClient(new FakeGetHandler(Array.Empty<byte>()))));

            Assert.Throws<BinaryUpdateException>(() =>
                service.ApplyUpdate(Path.Combine(dir, "does_not_exist.exe"), targetPath));

            Assert.Equal(new byte[] { 1, 1, 1 }, File.ReadAllBytes(targetPath)); // untouched
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static BinaryUpdateService BuildService(string releaseJson)
    {
        var checker = new Updates.GitHubReleaseChecker(new HttpClient(new FakeGetHandler(Encoding.UTF8.GetBytes(releaseJson), isJson: true)));
        var downloader = new ModelDownloader(new HttpClient(new FakeGetHandler(Array.Empty<byte>())));
        return new BinaryUpdateService(checker, downloader);
    }

    private static byte[] BuildZipWithSingleFile(string entryName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }
        return ms.ToArray();
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KeryxBinaryUpdateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class FakeGetHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        private readonly bool _isJson;

        public FakeGetHandler(byte[] body, bool isJson = false)
        {
            _body = body;
            _isJson = isJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body),
            };
            response.Content.Headers.ContentLength = _body.Length;
            if (_isJson) response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
