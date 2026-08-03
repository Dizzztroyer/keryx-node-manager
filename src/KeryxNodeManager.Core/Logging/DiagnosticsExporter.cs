using System.IO.Compression;
using System.Text.Json;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.Secrets;

namespace KeryxNodeManager.Core.Logging;

/// <summary>
/// Bundles log files + a redacted settings snapshot + basic system info into one ZIP a user can
/// attach to a bug report (brief §12). Two safety rules this class exists to enforce:
///
/// 1. Never include a raw mining address, environment variable, or model download URL/checksum
///    verbatim - RedactSettings runs every profile through SecretMasker.MaskAddress and strips
///    EnvironmentVariables entirely (a user could have put anything in there; this app has no way
///    to know it's safe to export).
/// 2. Never include the log *source* files directly from wherever they happen to live on disk
///    without re-reading them - they are copied into the archive at export time, so a partially
///    written line mid-append can't corrupt the exporter's own state (each source file is only
///    opened for read).
/// </summary>
public static class DiagnosticsExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Creates (overwriting if present) a ZIP at outputZipPath containing every *.log file
    /// in logsDirectory under "logs/", a redacted copy of settings under "settings-redacted.json",
    /// and "system-info.txt". Returns outputZipPath for convenience.</summary>
    public static string Export(string logsDirectory, string outputZipPath, AppSettings settings, string appVersion)
    {
        var outputDir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrEmpty(outputDir)) Directory.CreateDirectory(outputDir);

        if (File.Exists(outputZipPath)) File.Delete(outputZipPath);

        using var archive = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);

        if (Directory.Exists(logsDirectory))
        {
            foreach (var file in Directory.GetFiles(logsDirectory, "*.log"))
            {
                archive.CreateEntryFromFile(file, "logs/" + Path.GetFileName(file));
            }
        }

        WriteJsonEntry(archive, "settings-redacted.json", RedactSettings(settings));
        WriteTextEntry(archive, "system-info.txt", BuildSystemInfo(appVersion));

        return outputZipPath;
    }

    /// <summary>Deep-copies settings via a JSON round trip (so the live, in-memory settings object
    /// is never mutated by redaction) and masks/strips anything that must not leave the machine.
    /// Public (not internal) because it is independently useful/testable and this project doesn't
    /// otherwise wire up InternalsVisibleTo for the test assembly.</summary>
    public static AppSettings RedactSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var clone = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

        foreach (var profile in clone.Profiles)
        {
            if (!string.IsNullOrWhiteSpace(profile.MiningAddress))
            {
                profile.MiningAddress = SecretMasker.MaskAddress(profile.MiningAddress);
            }
            // Arbitrary user-supplied values this app has no way to know are safe to export.
            profile.EnvironmentVariables.Clear();
            profile.ModelSources.Clear();
        }

        return clone;
    }

    private static string BuildSystemInfo(string appVersion)
    {
        return string.Join(Environment.NewLine,
            $"Keryx Node Manager version: {appVersion}",
            $"OS: {Environment.OSVersion}",
            $".NET runtime: {Environment.Version}",
            $"64-bit OS: {Environment.Is64BitOperatingSystem}",
            $"Exported at (UTC): {DateTimeOffset.UtcNow:O}");
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteJsonEntry<T>(ZipArchive archive, string entryName, T value)
    {
        WriteTextEntry(archive, entryName, JsonSerializer.Serialize(value, JsonOptions));
    }
}
