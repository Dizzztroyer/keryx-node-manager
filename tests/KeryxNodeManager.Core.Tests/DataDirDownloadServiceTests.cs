using System.IO.Compression;
using System.Net;
using System.Text;
using KeryxNodeManager.Core.Networking;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers DataDirDownloadService's HTTP path (download a real in-memory-built zip via a fake
/// HttpMessageHandler, then extract it for real via System.IO.Compression) and the URL-suffix
/// dispatch between HTTP and torrent. The torrent path itself (MonoTorrent against a real
/// .torrent/swarm) is NOT covered here - that would need a real seeder, which is out of scope for
/// a unit test; IsTorrentUrl's dispatch logic is what's covered instead, since a wrong dispatch
/// here would silently send a multi-GB HTTP zip through the torrent code path or vice versa.
/// </summary>
public class DataDirDownloadServiceTests
{
    [Theory]
    [InlineData("https://keryx-labs.com/datadir.zip.torrent", true)]
    [InlineData("https://keryx-labs.com/datadir.zip", false)]
    [InlineData("https://huggingface.co/datasets/Keryx-Labs/datadir/resolve/main/datadir.zip", false)]
    [InlineData("https://KERYX-LABS.com/DATADIR.ZIP.TORRENT", true)] // case-insensitive suffix match
    public void IsTorrentUrl_DispatchesByUrlSuffix(string url, bool expectedIsTorrent)
    {
        Assert.Equal(expectedIsTorrent, DataDirDownloadService.IsTorrentUrl(url));
    }

    [Fact]
    public async Task DownloadAndExtractAsync_HttpZip_ExtractsFilesIntoTargetDirectory()
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "KeryxDataDirTests_target_" + Guid.NewGuid());
        try
        {
            var zipBytes = BuildZipWithFiles(new()
            {
                ["consensus/db.dat"] = Encoding.UTF8.GetBytes("fake-consensus-data"),
                ["utxo/index.dat"] = Encoding.UTF8.GetBytes("fake-utxo-data"),
            });
            var service = new DataDirDownloadService(new HttpClient(new FakeGetHandler(zipBytes)));

            await service.DownloadAndExtractAsync(
                new Uri("http://fake.local/datadir.zip"), targetDir, progress: null, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(targetDir, "consensus", "db.dat")));
            Assert.Equal("fake-consensus-data", await File.ReadAllTextAsync(Path.Combine(targetDir, "consensus", "db.dat")));
            Assert.True(File.Exists(Path.Combine(targetDir, "utxo", "index.dat")));
        }
        finally
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndExtractAsync_TargetDirectoryAlreadyHasOldContent_WipesBeforeExtracting()
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "KeryxDataDirTests_target_" + Guid.NewGuid());
        Directory.CreateDirectory(targetDir);
        var staleFile = Path.Combine(targetDir, "stale_from_previous_download.dat");
        await File.WriteAllTextAsync(staleFile, "old data that should not survive");

        try
        {
            var zipBytes = BuildZipWithFiles(new() { ["fresh.dat"] = Encoding.UTF8.GetBytes("new") });
            var service = new DataDirDownloadService(new HttpClient(new FakeGetHandler(zipBytes)));

            await service.DownloadAndExtractAsync(
                new Uri("http://fake.local/datadir.zip"), targetDir, progress: null, CancellationToken.None);

            Assert.False(File.Exists(staleFile), "stale data from a prior download must not silently linger alongside new data");
            Assert.True(File.Exists(Path.Combine(targetDir, "fresh.dat")));
        }
        finally
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true);
        }
    }

    private static byte[] BuildZipWithFiles(Dictionary<string, byte[]> files)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }

    private sealed class FakeGetHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        public FakeGetHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_body) };
            response.Content.Headers.ContentLength = _body.Length;
            return Task.FromResult(response);
        }
    }
}
