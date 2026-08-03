using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace KeryxNodeManager.Core.ModelsManagement;

/// <summary>
/// Drives the Models page's own download of a model file (brief §7: progress/pause/resume/
/// checksum UI). This is deliberately NOT the miner's built-in IPFS auto-download -
/// docs/KERYX_RESEARCH.md §3/§7 confirms the miner has no IPC for download progress, so a
/// UI-visible download has to be one the app performs itself, over plain HTTP(S), against
/// whatever mirror URL the user supplies (the miner's own README lists HuggingFace/direct/
/// torrent mirrors, but this app does not hardcode any of them - see PROJECT_STATUS.md for why:
/// no verified URL/checksum was captured during research, and shipping a guessed one would be
/// worse than requiring the user to paste a URL they trust).
///
/// "Pause" and "Resume" are not distinct code paths here: this class always resumes from
/// whatever partial bytes already exist at ModelFileLocator.GetPartialPath (via an HTTP Range
/// request), and cancelling the CancellationToken simply stops mid-stream leaving that partial
/// file in place - the caller (ModelsViewModel) decides whether "pause" (do nothing further) or
/// "cancel" (also delete the .part file) is what the user asked for.
/// </summary>
public sealed class ModelDownloader
{
    private readonly HttpClient _httpClient;

    public ModelDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <param name="source">Mirror URL the user supplied for this tier.</param>
    /// <param name="destinationPath">Final path, e.g. ModelFileLocator.GetModelPath(...).</param>
    /// <param name="progress">Reports cumulative bytes received (including bytes from a prior,
    /// resumed session) and the total size if known.</param>
    /// <param name="expectedSha256Hex">Optional - if supplied, verified after the transfer
    /// completes and BEFORE the file is exposed at destinationPath; a mismatch deletes the
    /// partial file and throws rather than leaving a corrupt/mislabeled model in place.</param>
    public async Task DownloadAsync(
        Uri source,
        string destinationPath,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct,
        string? expectedSha256Hex = null)
    {
        var partialPath = destinationPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");

        long existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        // A server that doesn't support Range silently returns 200 with the WHOLE file instead of
        // 206 with just the remainder - if we appended to the partial file in that case we'd
        // produce a corrupt, doubled-up result. Detect this and restart from zero instead.
        bool resumedFromServer = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (existingLength > 0 && !resumedFromServer)
        {
            existingLength = 0;
        }

        response.EnsureSuccessStatusCode();

        long? totalBytes = ResolveTotalBytes(response, existingLength);

        var fileMode = resumedFromServer ? FileMode.Append : FileMode.Create;
        await using (var fileStream = new FileStream(partialPath, fileMode, FileAccess.Write, FileShare.None))
        await using (var responseStream = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[81920];
            long totalWritten = existingLength;
            progress?.Report(new ModelDownloadProgress(totalWritten, totalBytes));

            int read;
            while ((read = await responseStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalWritten += read;
                progress?.Report(new ModelDownloadProgress(totalWritten, totalBytes));
            }
        }

        if (expectedSha256Hex is not null)
        {
            var actualHex = await ComputeSha256HexAsync(partialPath, ct);
            if (!string.Equals(actualHex, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partialPath);
                throw new ModelChecksumMismatchException(expectedSha256Hex, actualHex);
            }
        }

        // Atomic-ish handoff: the miner should never see a half-written file at destinationPath -
        // it only ever appears there once the transfer (and optional checksum) is fully done.
        File.Move(partialPath, destinationPath, overwrite: true);
    }

    /// <summary>Deletes a partial download outright - used by the "Cancel" (not "Pause") command
    /// so a fully-abandoned download doesn't leave a stray multi-GB .part file behind.</summary>
    public static void DeletePartial(string destinationPath)
    {
        var partialPath = destinationPath + ".part";
        if (File.Exists(partialPath)) File.Delete(partialPath);
    }

    private static long? ResolveTotalBytes(HttpResponseMessage response, long existingLength)
    {
        // 206 Partial Content: Content-Range: bytes {start}-{end}/{total} - {total} is the whole
        // file's size, not just the remainder.
        if (response.Content.Headers.ContentRange?.Length is long totalFromRange)
        {
            return totalFromRange;
        }

        // 200 OK with a fresh/restarted download: Content-Length is the whole file.
        if (response.Content.Headers.ContentLength is long contentLength)
        {
            return existingLength + contentLength;
        }

        return null; // server didn't report a size - UI shows an indeterminate progress state.
    }

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
