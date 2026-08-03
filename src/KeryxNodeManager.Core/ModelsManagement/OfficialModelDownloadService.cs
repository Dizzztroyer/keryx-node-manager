using System.IO.Compression;
using KeryxNodeManager.Core.Models;
using MonoTorrent;
using MonoTorrent.Client;

namespace KeryxNodeManager.Core.ModelsManagement;

/// <summary>Progress reported by <see cref="OfficialModelDownloadService"/> - mirrors
/// <see cref="Networking.DataDirDownloadProgress"/>'s shape/phase-string convention so the App
/// layer can reuse the same progress-bar pattern already used for the data-dir download and the
/// binary updater.</summary>
public sealed record OfficialModelDownloadProgress(long BytesReceived, long? TotalBytes, string Phase);

/// <summary>
/// One-click "download this tier's official model archive and install it" (brief §7 follow-up:
/// the existing Models page only accepted a manually-pasted URL - see ModelCardViewModel's own doc
/// comment - because no verified official mirror existed yet. <see cref="OfficialModelMirrors"/>
/// now holds real, individually live-tested mirrors, so this service can offer a genuine one-click
/// path alongside the manual-URL field, which remains for anyone who wants a different/newer
/// mirror than the ones baked in here.
///
/// Structurally this mirrors Networking.DataDirDownloadService (same HTTP-vs-torrent dispatch by
/// URL suffix, same MonoTorrent usage) with one deliberate difference: it does NOT wipe the target
/// directory first. The models directory holds every tier side-by-side
/// ("&lt;modelsDir&gt;/&lt;DirName&gt;/model.gguf" per tier - see ModelFileLocator), so clearing the
/// whole directory to install one tier would delete every other already-installed model. Each
/// official archive's zip root folder name matches its tier's ModelSpec.DirName exactly (confirmed
/// by reading GLM-4-9B-0414.zip's real remote central directory - see OfficialModelMirrors' doc
/// comment), so extracting straight into the configured models directory root places the file at
/// exactly the path ModelFileLocator expects, with no renaming step and no risk to sibling tiers.
/// </summary>
public sealed class OfficialModelDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly ModelDownloader _httpDownloader;

    public OfficialModelDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpDownloader = new ModelDownloader(httpClient);
    }

    public async Task DownloadAndInstallAsync(
        ModelSpec spec,
        string source,
        string modelsDirectory,
        IProgress<OfficialModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(modelsDirectory);
        var workDir = Path.Combine(Path.GetTempPath(), "KeryxNodeManagerModels", spec.DirName);
        Directory.CreateDirectory(workDir);
        var zipPath = Path.Combine(workDir, spec.DirName + ".zip");
        var sourceUri = new Uri(source);

        if (source.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            await DownloadViaTorrentAsync(sourceUri, zipPath, workDir, progress, ct);
        }
        else
        {
            await DownloadViaHttpAsync(sourceUri, zipPath, progress, ct);
        }

        progress?.Report(new OfficialModelDownloadProgress(0, null, "Распаковка..."));

        // The tier's own subfolder (if a previous partial/manual install left one) is replaced, not
        // merged - a half-old, half-new model folder could silently mix a stale .gguf with a fresh
        // .ok marker or vice versa. Sibling tiers' folders are untouched (extraction only writes
        // paths that fall under this archive's own DirName root).
        var tierFolder = Path.Combine(modelsDirectory, spec.DirName);
        if (Directory.Exists(tierFolder))
        {
            Directory.Delete(tierFolder, recursive: true);
        }
        ZipFile.ExtractToDirectory(zipPath, modelsDirectory, overwriteFiles: true);

        File.Delete(zipPath);

        if (!ModelFileLocator.IsInstalled(modelsDirectory, spec.DirName))
        {
            // The official archive's internal layout was verified once (see OfficialModelMirrors'
            // doc comment) but a live mirror could still change shape later - this must fail loudly
            // rather than silently report success for a model the miner will not actually find.
            throw new OfficialModelDownloadException(
                $"Архив скачан и распакован, но ожидаемый файл {ModelFileLocator.GetModelPath(modelsDirectory, spec.DirName)} " +
                "не найден - формат официального архива мог измениться.");
        }
    }

    private async Task DownloadViaHttpAsync(
        Uri source, string zipPath, IProgress<OfficialModelDownloadProgress>? progress, CancellationToken ct)
    {
        var inner = progress is null
            ? null
            : new Progress<ModelDownloadProgress>(p =>
                progress.Report(new OfficialModelDownloadProgress(p.BytesReceived, p.TotalBytes, "Скачивание (HTTP)...")));
        await _httpDownloader.DownloadAsync(source, zipPath, inner, ct);
    }

    private async Task DownloadViaTorrentAsync(
        Uri torrentUrl, string zipPath, string workDir,
        IProgress<OfficialModelDownloadProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new OfficialModelDownloadProgress(0, null, "Загрузка .torrent-файла..."));
        var torrentFilePath = Path.Combine(workDir, "model.torrent");
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
                progress?.Report(new OfficialModelDownloadProgress(received, total, "Скачивание (torrent)..."));
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        finally
        {
            await manager.StopAsync();
        }

        var downloadedFile = torrent.Files.Count == 1
            ? Path.Combine(workDir, torrent.Files[0].Path)
            : null;
        if (downloadedFile is null || !File.Exists(downloadedFile))
        {
            throw new OfficialModelDownloadException(
                "Торрент скачан, но ожидаемый единственный .zip-файл не найден - формат раздачи мог измениться.");
        }
        if (!string.Equals(downloadedFile, zipPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(downloadedFile, zipPath, overwrite: true);
        }
    }
}

public sealed class OfficialModelDownloadException(string message) : Exception(message);
