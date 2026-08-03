using System.IO.Compression;
using System.Net;
using System.Text;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.ModelsManagement;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers OfficialModelDownloadService's HTTP path (real System.IO.Compression extraction against
/// an in-memory-built zip served by a fake HttpMessageHandler) and, critically, that it does NOT
/// wipe the whole models directory the way DataDirDownloadService wipes its target - siblings
/// tiers already installed alongside the one being (re)installed must survive, since one models
/// directory holds every tier side by side (see ModelFileLocator). The torrent path itself is not
/// covered here for the same reason DataDirDownloadServiceTests doesn't cover it - MonoTorrent
/// against a real swarm needs a real seeder, out of scope for a unit test.
/// </summary>
public class OfficialModelDownloadServiceTests
{
    private static readonly ModelSpec TestSpec = new(
        ModelTier.Default, "GLM-4-9B-0414", "GLM-4-9B-0414", "Q6_K", "", 12_000);

    private static readonly ModelSpec OtherSpec = new(
        ModelTier.Light, "Mistral-7B-v0.3", "Mistral-7B-v0.3", "Q6_K", "--light", 8_000);

    [Fact]
    public async Task DownloadAndInstallAsync_HttpZip_ExtractsModelFileAtExpectedPath()
    {
        var modelsDir = Path.Combine(Path.GetTempPath(), "KeryxOfficialModelTests_" + Guid.NewGuid());
        try
        {
            var zipBytes = BuildZipWithFiles(new()
            {
                ["GLM-4-9B-0414/.ok"] = Array.Empty<byte>(),
                ["GLM-4-9B-0414/model.gguf"] = Encoding.UTF8.GetBytes("fake-model-bytes"),
            });
            var service = new OfficialModelDownloadService(new HttpClient(new FakeGetHandler(zipBytes)));

            await service.DownloadAndInstallAsync(
                TestSpec, "http://fake.local/GLM-4-9B-0414.zip", modelsDir, progress: null, CancellationToken.None);

            Assert.True(ModelFileLocator.IsInstalled(modelsDir, TestSpec.DirName));
            Assert.Equal("fake-model-bytes",
                await File.ReadAllTextAsync(ModelFileLocator.GetModelPath(modelsDir, TestSpec.DirName)));
        }
        finally
        {
            if (Directory.Exists(modelsDir)) Directory.Delete(modelsDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndInstallAsync_SiblingTierAlreadyInstalled_IsNotDeleted()
    {
        // The whole reason this class does NOT wipe modelsDirectory before extracting (unlike
        // DataDirDownloadService, which owns its entire target directory exclusively) - every tier
        // shares one models directory, so installing/reinstalling one tier must never touch
        // another tier's already-downloaded, possibly multi-GB file.
        var modelsDir = Path.Combine(Path.GetTempPath(), "KeryxOfficialModelTests_" + Guid.NewGuid());
        try
        {
            var otherModelPath = ModelFileLocator.GetModelPath(modelsDir, OtherSpec.DirName);
            Directory.CreateDirectory(Path.GetDirectoryName(otherModelPath)!);
            await File.WriteAllTextAsync(otherModelPath, "pre-existing sibling tier - must survive");

            var zipBytes = BuildZipWithFiles(new()
            {
                ["GLM-4-9B-0414/model.gguf"] = Encoding.UTF8.GetBytes("fake-model-bytes"),
            });
            var service = new OfficialModelDownloadService(new HttpClient(new FakeGetHandler(zipBytes)));

            await service.DownloadAndInstallAsync(
                TestSpec, "http://fake.local/GLM-4-9B-0414.zip", modelsDir, progress: null, CancellationToken.None);

            Assert.True(File.Exists(otherModelPath));
            Assert.Equal("pre-existing sibling tier - must survive", await File.ReadAllTextAsync(otherModelPath));
            Assert.True(ModelFileLocator.IsInstalled(modelsDir, TestSpec.DirName));
        }
        finally
        {
            if (Directory.Exists(modelsDir)) Directory.Delete(modelsDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndInstallAsync_ArchiveMissingExpectedModelFile_ThrowsRatherThanSilentlySucceeding()
    {
        var modelsDir = Path.Combine(Path.GetTempPath(), "KeryxOfficialModelTests_" + Guid.NewGuid());
        try
        {
            // Simulates a mirror whose internal layout drifted from what OfficialModelMirrors'
            // doc comment recorded as verified - must fail loudly, not report success for a model
            // the miner will never find.
            var zipBytes = BuildZipWithFiles(new() { ["GLM-4-9B-0414/readme.txt"] = Encoding.UTF8.GetBytes("oops") });
            var service = new OfficialModelDownloadService(new HttpClient(new FakeGetHandler(zipBytes)));

            await Assert.ThrowsAsync<OfficialModelDownloadException>(() => service.DownloadAndInstallAsync(
                TestSpec, "http://fake.local/GLM-4-9B-0414.zip", modelsDir, progress: null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(modelsDir)) Directory.Delete(modelsDir, recursive: true);
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

/// <summary>
/// Covers OfficialModelMirrors' bundled table - not the live URLs themselves (that was verified
/// once, out-of-band, by real curl/HTTP checks against keryx-labs.com and Hugging Face on
/// 2026-08-03, see the class's own doc comment), but that the table is well-formed: every
/// ModelTier has an entry, every entry's DirectUrl actually matches that tier's
/// ModelSpec.DirName (a mismatch here would silently install the wrong model under the wrong
/// tier's expected path), and the one known torrent-less tier (VeryLight) is exactly that.
/// </summary>
public class OfficialModelMirrorsTests
{
    [Fact]
    public void Mirrors_HasEntryForEveryTier()
    {
        foreach (var spec in ModelTierCatalog.Tiers)
        {
            var mirror = OfficialModelMirrors.TryGet(spec.Tier);
            Assert.NotNull(mirror);
            Assert.Contains(spec.DirName, mirror!.DirectUrl);
        }
    }

    [Fact]
    public void Mirrors_AllUrlsAreAbsoluteHttps()
    {
        foreach (var mirror in OfficialModelMirrors.Mirrors.Values)
        {
            Assert.True(Uri.TryCreate(mirror.DirectUrl, UriKind.Absolute, out var directUri));
            Assert.Equal(Uri.UriSchemeHttps, directUri!.Scheme);

            if (mirror.TorrentUrl is not null)
            {
                Assert.True(Uri.TryCreate(mirror.TorrentUrl, UriKind.Absolute, out var torrentUri));
                Assert.Equal(Uri.UriSchemeHttps, torrentUri!.Scheme);
                Assert.EndsWith(".torrent", mirror.TorrentUrl, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void VeryLightTier_HasNoTorrentMirror_OthersDo()
    {
        // Documents a real, verified-at-the-time asymmetry (see class doc comment): keryx-labs.com's
        // EXAONE-4.0-1.2B replacement for VeryLight was announced but 404'd on every variant tried,
        // so VeryLight uses the Hugging Face direct URL only. If this ever flips (a real torrent
        // shows up, or another tier's mirror goes down), this test's failure is the intended signal
        // to go re-verify and update OfficialModelMirrors, not to just loosen the assertion.
        Assert.Null(OfficialModelMirrors.TryGet(ModelTier.VeryLight)!.TorrentUrl);
        Assert.NotNull(OfficialModelMirrors.TryGet(ModelTier.Light)!.TorrentUrl);
        Assert.NotNull(OfficialModelMirrors.TryGet(ModelTier.Default)!.TorrentUrl);
        Assert.NotNull(OfficialModelMirrors.TryGet(ModelTier.High)!.TorrentUrl);
        Assert.NotNull(OfficialModelMirrors.TryGet(ModelTier.VeryHigh)!.TorrentUrl);
    }
}
