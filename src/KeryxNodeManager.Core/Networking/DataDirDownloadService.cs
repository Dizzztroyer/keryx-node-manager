using System.IO.Compression;
using KeryxNodeManager.Core.ModelsManagement;
using MonoTorrent;
using MonoTorrent.Client;

namespace KeryxNodeManager.Core.Networking;

/// <summary>Progress reported by <see cref="DataDirDownloadService"/> - deliberately mirrors
/// <see cref="ModelDownloadProgress"/>'s shape (BytesReceived/TotalBytes) so the App layer can
/// reuse the same progress-bar binding pattern as the binary-updater and Models page.</summary>
public sealed record DataDirDownloadProgress(long BytesReceived, long? TotalBytes, string Phase);

/// <summary>
/// One-click "download the Keryx blockchain data directory and point keryxd at it" (user request:
/// paste a link, it downloads, extracts, and the node syncs from there). keryxd itself has NO
/// built-in snapshot/bootstrap mechanism (confirmed - neither the repo nor docs/archival.md mention
/// one; Kaspa-derived chains normally sync peer-to-peer from genesis). The actual real source for
/// this is the Keryx-Labs dev team's own recovery instructions, posted in their Discord whenever
/// the network needs a coordinated restart, e.g.:
///   - https://huggingface.co/datasets/Keryx-Labs/datadir/resolve/main/datadir.zip (HTTP)
///   - https://keryx-labs.com/datadir.zip (HTTP)
///   - https://keryx-labs.com/datadir.zip.torrent (BitTorrent, via a .torrent metadata file)
///   - a Google Drive share link (deliberately NOT supported here - large-file Google Drive
///     downloads require an interactive HTML confirmation-token dance that breaks under
///     automation and isn't a stable API contract to build against; direct the user to one of the
///     other three mirrors instead)
///
/// This app does not hardcode any of the above as a default/pre-filled URL - the dev team's own
/// message noted this is a recurring "here we go again" event tied to specific restarts, so any
/// URL baked in today could be stale by the time this ships. The user pastes whatever mirror link
/// is current; this class only tells .torrent from direct-HTTP by URL suffix (falling back to
/// content-type sniffing is deliberately NOT done - a wrong guess here would silently start the
/// wrong download path for a multi-GB transfer).
/// </summary>
public sealed class DataDirDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly ModelDownloader _httpDownloader;

    public DataDirDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpDownloader = new ModelDownloader(httpClient);
    }

    public static bool IsTorrentUrl(string url) =>
        url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);

    /// <summary>Downloads (HTTP or torrent, dispatched by <see cref="IsTorrentUrl"/>) and extracts
    /// a data-dir archive into <paramref name="targetDirectory"/>. Always extracts into a fresh
    /// directory (wipes any prior contents first) rather than merging - a half-old, half-new data
    /// directory is exactly the kind of silent corruption this feature exists to avoid.</summary>
    public async Task DownloadAndExtractAsync(
        Uri source,
        string targetDirectory,
        IProgress<DataDirDownloadProgress>? progress,
        CancellationToken ct)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "KeryxNodeManagerDataDir");
        Directory.CreateDirectory(workDir);
        var zipPath = Path.Combine(workDir, "datadir-download.zip");

        if (IsTorrentUrl(source.ToString()))
        {
            await DownloadViaTorrentAsync(source, zipPath, workDir, progress, ct);
        }
        else
        {
            await DownloadViaHttpAsync(source, zipPath, progress, ct);
        }

        progress?.Report(new DataDirDownloadProgress(0, null, "Распаковка..."));

        if (Directory.Exists(targetDirectory))
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
        Directory.CreateDirectory(targetDirectory);
        ZipFile.ExtractToDirectory(zipPath, targetDirectory, overwriteFiles: true);

        File.Delete(zipPath);
    }

    private async Task DownloadViaHttpAsync(
        Uri source, string zipPath, IProgress<DataDirDownloadProgress>? progress, CancellationToken ct)
    {
        var inner = progress is null
            ? null
            : new Progress<ModelDownloadProgress>(p =>
                progress.Report(new DataDirDownloadProgress(p.BytesReceived, p.TotalBytes, "Скачивание (HTTP)...")));
        await _httpDownloader.DownloadAsync(source, zipPath, inner, ct);
    }

    private async Task DownloadViaTorrentAsync(
        Uri torrentUrl, string zipPath, string workDir,
        IProgress<DataDirDownloadProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new DataDirDownloadProgress(0, null, "Загрузка .torrent-файла..."));
        var torrentFilePath = Path.Combine(workDir, "datadir.torrent");
        var torrentBytes = await _httpClient.GetByteArrayAsync(torrentUrl, ct);
        await File.WriteAllBytesAsync(torrentFilePath, torrentBytes, ct);

        var torrent = await Torrent.LoadAsync(torrentFilePath);

        var engineSettings = new EngineSettingsBuilder
        {
            CacheDirectory = workDir,
        }.ToSettings();
        using var engine = new ClientEngine(engineSettings);

        var manager = await engine.AddAsync(torrent, workDir);
        await manager.StartAsync();

        try
        {
            while (manager.Complete is false)
            {
                ct.ThrowIfCancellationRequested();
                var total = torrent.Size;
                var received = (long)(total * (manager.Progress / 100.0));
                progress?.Report(new DataDirDownloadProgress(received, total, "Скачивание (torrent)..."));
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        finally
        {
            await manager.StopAsync();
        }

        // The torrent's own file layout should match the expected single "datadir.zip" - if the
        // real torrent ever changes shape (e.g. distributes an already-unpacked folder instead of
        // one zip), this is exactly the kind of drift that must fail loudly, not silently produce
        // an empty/wrong result.
        var downloadedFile = torrent.Files.Count == 1
            ? Path.Combine(workDir, torrent.Files[0].Path)
            : null;
        if (downloadedFile is null || !File.Exists(downloadedFile))
        {
            throw new DataDirDownloadException(
                "Торрент скачан, но ожидаемый единственный .zip-файл не найден - формат раздачи мог измениться.");
        }
        if (!string.Equals(downloadedFile, zipPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(downloadedFile, zipPath, overwrite: true);
        }
    }
}

public sealed class DataDirDownloadException(string message) : Exception(message);
