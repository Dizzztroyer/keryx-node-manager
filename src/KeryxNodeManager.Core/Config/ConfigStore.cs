using System.Text.Json;
using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.Config;

/// <summary>
/// Loads/saves AppSettings with atomic writes so a power loss or crash mid-save cannot corrupt
/// the config (brief §18). Save path: write to a temp file in the same directory (same volume,
/// so the following move is atomic on NTFS), flush+fsync, then File.Replace the real file
/// (keeping a .bak of the previous version). Load path: validates SchemaVersion and runs
/// registered migrations, in order, before handing the object to the caller.
/// </summary>
public sealed class ConfigStore
{
    private readonly string _settingsPath;
    private readonly IReadOnlyList<IConfigMigration> _migrations;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConfigStore(string settingsPath, IReadOnlyList<IConfigMigration>? migrations = null)
    {
        _settingsPath = settingsPath;
        _migrations = migrations ?? Array.Empty<IConfigMigration>();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var raw = doc.RootElement.Clone();

        int schemaVersion = raw.TryGetProperty("SchemaVersion", out var v) ? v.GetInt32() : 0;

        var settings = raw.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
        settings.SchemaVersion = schemaVersion;

        foreach (var migration in _migrations.OrderBy(m => m.FromVersion))
        {
            if (settings.SchemaVersion == migration.FromVersion)
            {
                settings = migration.Apply(settings);
                settings.SchemaVersion = migration.ToVersion;
            }
        }

        return settings;
    }

    public async Task SaveAtomicAsync(AppSettings settings, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_settingsPath)}.tmp-{Guid.NewGuid():N}");
        var backupPath = _settingsPath + ".bak";

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(_settingsPath))
        {
            File.Replace(tempPath, _settingsPath, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, _settingsPath);
        }
    }
}

public interface IConfigMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    AppSettings Apply(AppSettings settings);
}
