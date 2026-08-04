using System.IO.Compression;
using KeryxNodeManager.Core.Localization;
using KeryxNodeManager.Core.ModelsManagement;

namespace KeryxNodeManager.Core.Updates;

/// <summary>Result of comparing the locally recorded installed version against the latest
/// upstream release. `InstalledVersion` is null when this app has never recorded a version for
/// the binary at this path (e.g. the user pointed at an executable they installed manually before
/// ever using this feature) - `UpdateAvailable` is still meaningfully true in that case (there IS
/// a newer release than "nothing we know of"), the UI just can't show a "vX -&gt; vY" diff, only
/// "latest is vY".</summary>
public sealed record BinaryUpdateCheckResult(
    ManagedBinaryKind Kind,
    string? InstalledVersion,
    string LatestVersion,
    bool UpdateAvailable,
    Uri? DownloadUrl,
    string? AssetName);

/// <summary>
/// Checks for and applies updates to the keryxd.exe/keryx-miner.exe binaries this app manages, by
/// polling their real upstream GitHub repos (Keryx-Labs/keryx-node, Keryx-Labs/keryx-miner) -
/// this is a different concern from KeryxNodeManager's own app version, which has no auto-update
/// story at all yet (see PROJECT_STATUS.md/docs/RELEASE.md).
///
/// Nothing in this class ever applies an update without the caller explicitly driving it through
/// both DownloadAndExtractAsync and then ApplyUpdateAsync - there is no single "just update it"
/// method, deliberately: replacing a binary the user might currently be running for real mining
/// is not something this app does silently in the background. The caller (App layer) is
/// responsible for stopping the managed process (ProcessSupervisor.StopAsync) before calling
/// ApplyUpdateAsync - this class only touches files, never process state, keeping it consistent
/// with this project's existing separation between Process/ and the rest of Core.
/// </summary>
public sealed class BinaryUpdateService
{
    private readonly GitHubReleaseChecker _releaseChecker;
    private readonly ModelDownloader _downloader;

    public BinaryUpdateService(GitHubReleaseChecker releaseChecker, ModelDownloader downloader)
    {
        _releaseChecker = releaseChecker;
        _downloader = downloader;
    }

    public async Task<BinaryUpdateCheckResult> CheckAsync(
        ManagedBinaryKind kind, string? installedVersion, CancellationToken ct = default)
    {
        var repo = KeryxRepos.RepoNameFor(kind);
        var release = await _releaseChecker.GetLatestReleaseAsync(KeryxRepos.Owner, repo, ct);
        var asset = release.FindWindowsAsset();

        bool updateAvailable = !string.Equals(installedVersion, release.TagName, StringComparison.OrdinalIgnoreCase);

        return new BinaryUpdateCheckResult(
            kind, installedVersion, release.TagName, updateAvailable, asset?.DownloadUrl, asset?.Name);
    }

    /// <summary>
    /// Downloads the release zip (reusing the same resumable/checksummed download machinery the
    /// Models page already uses - see ModelDownloader's own doc comment) and extracts it into
    /// <paramref name="workDir"/>. Returns the path to the extracted exe, matched by exact
    /// filename first (KeryxRepos.ExeFileNameFor) and falling back to "the only .exe in the
    /// archive" if an exact match isn't found (in case a future release renames the binary).
    /// </summary>
    public async Task<string> DownloadAndExtractAsync(
        ManagedBinaryKind kind,
        Uri downloadUrl,
        string workDir,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(workDir);
        string zipPath = Path.Combine(workDir, "update.zip");
        string extractDir = Path.Combine(workDir, "extracted");

        // Every check-for-update call can point at a different release than the last one this
        // work dir was used for - a stale .part/.zip/extracted directory left over from a
        // previous, different version must never be silently reused (ModelDownloader's Range-resume
        // logic has no idea the bytes on disk belong to a different release entirely).
        ModelDownloader.DeletePartial(zipPath);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);

        await _downloader.DownloadAsync(downloadUrl, zipPath, progress, ct);
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        string expectedName = KeryxRepos.ExeFileNameFor(kind);
        string? exePath = Directory.EnumerateFiles(extractDir, expectedName, SearchOption.AllDirectories).FirstOrDefault();
        if (exePath is null)
        {
            var anyExe = Directory.EnumerateFiles(extractDir, "*.exe", SearchOption.AllDirectories).ToList();
            exePath = anyExe.Count == 1 ? anyExe[0] : null;
        }

        if (exePath is null)
        {
            throw new BinaryUpdateException(CoreStrings.Format("Update.ExeNotFoundInArchive", expectedName));
        }

        return exePath;
    }

    /// <summary>
    /// Replaces <paramref name="targetExePath"/> with the freshly downloaded/extracted exe at
    /// <paramref name="extractedExePath"/>. The caller MUST have already stopped any process
    /// running from targetExePath (Windows holds an exclusive lock on a running executable's file
    /// and this copy will throw an IOException otherwise - that failure mode is intentional, not
    /// suppressed, so a bug in the caller's "stop before replace" ordering surfaces immediately
    /// rather than silently corrupting a running process's image). The previous binary is backed
    /// up to `{targetExePath}.bak` (overwriting any earlier backup) before the copy, so a bad
    /// download/extract that still somehow passes this far can be manually rolled back.
    ///
    /// A release archive commonly ships more than just the exe alongside it - e.g. keryx-miner's
    /// GPU backend plugin DLLs and its LLM inference engine DLL, which MUST sit in the same
    /// directory as keryx-miner.exe or the miner silently loses functionality (mining fails with
    /// "No workers found") without this class ever seeing an error. Only ever copying the exe
    /// itself here (as this method originally did) meant every one of this app's own "Install
    /// update" runs would leave a newer exe paired with stale/missing plugin files the moment a
    /// release started bundling them - a real user hitting Update would get exactly this failure,
    /// not just a hand-patched dev machine. So this also copies every other file that was
    /// extracted alongside the exe into the target directory, keeping the installed exe and its
    /// plugins as one consistent set straight from the official release, every time.
    /// </summary>
    public void ApplyUpdate(string extractedExePath, string targetExePath)
    {
        if (!File.Exists(extractedExePath))
        {
            throw new BinaryUpdateException(CoreStrings.Format("Update.ExtractedExeMissing", extractedExePath));
        }

        var targetDir = Path.GetDirectoryName(targetExePath);
        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

        if (File.Exists(targetExePath))
        {
            File.Copy(targetExePath, targetExePath + ".bak", overwrite: true);
        }

        File.Copy(extractedExePath, targetExePath, overwrite: true);

        var extractedDir = Path.GetDirectoryName(extractedExePath);
        if (!string.IsNullOrEmpty(extractedDir) && !string.IsNullOrEmpty(targetDir))
        {
            foreach (var siblingFile in Directory.EnumerateFiles(extractedDir))
            {
                if (string.Equals(siblingFile, extractedExePath, StringComparison.OrdinalIgnoreCase)) continue;

                var destPath = Path.Combine(targetDir, Path.GetFileName(siblingFile));

                // extractedDir and targetDir can be the same directory (e.g. tests, or a caller
                // that extracts in place) - copying a file onto itself throws (Windows treats it
                // as a sharing violation), so skip when source and destination already resolve to
                // the same file rather than letting File.Copy attempt a self-copy.
                if (string.Equals(Path.GetFullPath(siblingFile), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase)) continue;

                File.Copy(siblingFile, destPath, overwrite: true);
            }
        }
    }
}

public sealed class BinaryUpdateException : Exception
{
    public BinaryUpdateException(string message) : base(message) { }
}
