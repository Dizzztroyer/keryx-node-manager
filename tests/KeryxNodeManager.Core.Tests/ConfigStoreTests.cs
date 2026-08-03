using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Models;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settingsPath;

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "knm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settingsPath = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenFileDoesNotExist()
    {
        var store = new ConfigStore(_settingsPath);
        var settings = await store.LoadAsync();
        Assert.Equal(1, settings.SchemaVersion);
        Assert.Empty(settings.Profiles);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsData()
    {
        var store = new ConfigStore(_settingsPath);
        var settings = new AppSettings { Language = "ru", StartWithWindows = true };
        settings.Profiles.Add(new MiningProfile { Name = "Test", MiningAddress = "keryx:abc" });

        await store.SaveAtomicAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal("ru", loaded.Language);
        Assert.True(loaded.StartWithWindows);
        Assert.Single(loaded.Profiles);
        Assert.Equal("Test", loaded.Profiles[0].Name);
    }

    [Fact]
    public async Task SaveAtomicAsync_KeepsBackupOfPreviousVersion()
    {
        var store = new ConfigStore(_settingsPath);
        await store.SaveAtomicAsync(new AppSettings { Language = "en" });
        await store.SaveAtomicAsync(new AppSettings { Language = "ru" });

        Assert.True(File.Exists(_settingsPath + ".bak"));
        var backupJson = await File.ReadAllTextAsync(_settingsPath + ".bak");
        Assert.Contains("\"en\"", backupJson);

        var currentJson = await File.ReadAllTextAsync(_settingsPath);
        Assert.Contains("\"ru\"", currentJson);
    }

    [Fact]
    public async Task SaveAtomicAsync_DoesNotLeaveTempFilesBehind()
    {
        var store = new ConfigStore(_settingsPath);
        await store.SaveAtomicAsync(new AppSettings());
        var leftoverTemps = Directory.GetFiles(_dir, "*.tmp-*");
        Assert.Empty(leftoverTemps);
    }

    [Fact]
    public async Task LoadAsync_AppliesMigrationsInOrder()
    {
        // Write a v0 document directly (simulating an old install) and confirm the migration
        // pipeline bumps it to v1 and applies the transformation.
        await File.WriteAllTextAsync(_settingsPath, "{\"SchemaVersion\":0,\"Language\":\"en\"}");

        var migration = new TestMigrationV0ToV1();
        var store = new ConfigStore(_settingsPath, new IConfigMigration[] { migration });
        var loaded = await store.LoadAsync();

        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal("ru", loaded.Language); // migration forces default language to ru
    }

    private sealed class TestMigrationV0ToV1 : IConfigMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 1;
        public AppSettings Apply(AppSettings settings)
        {
            settings.Language = "ru";
            return settings;
        }
    }
}
