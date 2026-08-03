using System.IO.Compression;
using KeryxNodeManager.Core.Logging;
using KeryxNodeManager.Core.Models;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// DiagnosticsExporter is what a user attaches to a bug report - these tests focus on the two
/// safety properties that matter most (never export a raw mining address or environment variable)
/// and on the export actually containing the log files that were on disk, so a "helpful" export
/// can't accidentally leak a secret or silently produce an empty ZIP.
/// </summary>
public class DiagnosticsExporterTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "keryx-diag-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void RedactSettings_MasksMiningAddressAndStripsEnvironmentVariables()
    {
        var settings = new AppSettings
        {
            Profiles =
            {
                new MiningProfile
                {
                    MiningAddress = "keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte",
                    EnvironmentVariables = { ["SOME_SECRET"] = "do-not-export-me" },
                },
            },
        };

        var redacted = DiagnosticsExporter.RedactSettings(settings);

        Assert.DoesNotContain("qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte",
            redacted.Profiles[0].MiningAddress);
        Assert.StartsWith("keryx:", redacted.Profiles[0].MiningAddress);
        Assert.Empty(redacted.Profiles[0].EnvironmentVariables);
    }

    [Fact]
    public void RedactSettings_DoesNotMutateOriginalSettings()
    {
        var original = new AppSettings
        {
            Profiles = { new MiningProfile { MiningAddress = "keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte" } },
        };

        _ = DiagnosticsExporter.RedactSettings(original);

        Assert.Equal("keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte",
            original.Profiles[0].MiningAddress);
    }

    [Fact]
    public void Export_BundlesLogFilesAndRedactedSettingsAndSystemInfo()
    {
        var logsDir = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(logsDir, "keryxd-2026-08-02.log"), "node output line");
            File.WriteAllText(Path.Combine(logsDir, "keryx-miner-2026-08-02.log"), "miner output line");

            var settings = new AppSettings
            {
                Profiles = { new MiningProfile { MiningAddress = "keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte" } },
            };
            var zipPath = Path.Combine(outDir, "diagnostics.zip");

            DiagnosticsExporter.Export(logsDir, zipPath, settings, appVersion: "0.1.0-test");

            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Select(e => e.FullName).ToList();

            Assert.Contains("logs/keryxd-2026-08-02.log", names);
            Assert.Contains("logs/keryx-miner-2026-08-02.log", names);
            Assert.Contains("settings-redacted.json", names);
            Assert.Contains("system-info.txt", names);

            var settingsJson = ReadEntry(archive, "settings-redacted.json");
            Assert.DoesNotContain("qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte", settingsJson);

            var systemInfo = ReadEntry(archive, "system-info.txt");
            Assert.Contains("0.1.0-test", systemInfo);
        }
        finally
        {
            Directory.Delete(logsDir, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Export_OverwritesAnExistingZipAtTheSamePath()
    {
        var logsDir = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            var zipPath = Path.Combine(outDir, "diagnostics.zip");
            File.WriteAllText(zipPath, "not a real zip, just a placeholder to overwrite");

            DiagnosticsExporter.Export(logsDir, zipPath, new AppSettings(), "0.1.0-test");

            using var archive = ZipFile.OpenRead(zipPath); // throws if the old placeholder wasn't replaced
            Assert.Contains(archive.Entries, e => e.FullName == "system-info.txt");
        }
        finally
        {
            Directory.Delete(logsDir, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
