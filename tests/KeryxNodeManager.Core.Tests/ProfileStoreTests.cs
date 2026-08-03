using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Models;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers the multi-profile management methods added for the Profiles page (switch/create/
/// rename/delete) - AppSettings already had Profiles/ActiveProfileName from the start, but
/// ProfileStore itself could previously only load-once/save-the-one-it-has, so this is genuinely
/// new behaviour, not a refactor of something already tested.
/// </summary>
public class ProfileStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settingsPath;

    public ProfileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "knm-profile-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settingsPath = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private async Task<ProfileStore> NewLoadedStoreAsync()
    {
        var store = new ProfileStore(new ConfigStore(_settingsPath));
        await store.LoadAsync();
        return store;
    }

    [Fact]
    public async Task LoadAsync_WithNoExistingFile_AutoCreatesOneDefaultProfile()
    {
        var store = await NewLoadedStoreAsync();
        Assert.Single(store.ProfileNames);
        Assert.Equal("Default", store.ActiveProfile.Name);
    }

    [Fact]
    public async Task CreateProfileAsync_AddsAndSwitchesToNewProfile()
    {
        var store = await NewLoadedStoreAsync();
        await store.CreateProfileAsync("Rig2");

        Assert.Equal(2, store.ProfileNames.Count);
        Assert.Contains("Rig2", store.ProfileNames);
        Assert.Equal("Rig2", store.ActiveProfile.Name);
    }

    [Fact]
    public async Task CreateProfileAsync_PersistsAcrossReload()
    {
        var store = await NewLoadedStoreAsync();
        await store.CreateProfileAsync("Rig2");

        var reloaded = new ProfileStore(new ConfigStore(_settingsPath));
        await reloaded.LoadAsync();

        Assert.Equal(2, reloaded.ProfileNames.Count);
        Assert.Equal("Rig2", reloaded.ActiveProfile.Name); // ActiveProfileName was persisted too
    }

    [Fact]
    public async Task CreateProfileAsync_RejectsDuplicateNameCaseInsensitive()
    {
        var store = await NewLoadedStoreAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateProfileAsync("default"));
    }

    [Fact]
    public async Task CreateProfileAsync_RejectsEmptyOrWhitespaceName()
    {
        var store = await NewLoadedStoreAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateProfileAsync("   "));
    }

    [Fact]
    public async Task SwitchActiveProfileAsync_ChangesActiveProfileAndPersists()
    {
        var store = await NewLoadedStoreAsync();
        await store.CreateProfileAsync("Rig2");
        await store.SwitchActiveProfileAsync("Default");

        Assert.Equal("Default", store.ActiveProfile.Name);

        var reloaded = new ProfileStore(new ConfigStore(_settingsPath));
        await reloaded.LoadAsync();
        Assert.Equal("Default", reloaded.ActiveProfile.Name);
    }

    [Fact]
    public async Task SwitchActiveProfileAsync_UnknownName_Throws()
    {
        var store = await NewLoadedStoreAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SwitchActiveProfileAsync("NoSuchProfile"));
    }

    [Fact]
    public async Task RenameProfileAsync_KeepsSameProfileDataUnderNewName()
    {
        var store = await NewLoadedStoreAsync();
        store.ActiveProfile.MiningAddress = "keryx:abc";
        await store.SaveAsync();

        await store.RenameProfileAsync("Default", "MainRig");

        Assert.Equal("MainRig", store.ActiveProfile.Name);
        Assert.Equal("keryx:abc", store.ActiveProfile.MiningAddress); // same instance, data intact
        Assert.DoesNotContain("Default", store.ProfileNames);
    }

    [Fact]
    public async Task RenameProfileAsync_RejectsDuplicateTargetName()
    {
        var store = await NewLoadedStoreAsync();
        await store.CreateProfileAsync("Rig2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenameProfileAsync("Rig2", "Default"));
    }

    [Fact]
    public async Task DeleteProfileAsync_RemovesNonActiveProfile()
    {
        var store = await NewLoadedStoreAsync();
        await store.CreateProfileAsync("Rig2");
        await store.SwitchActiveProfileAsync("Default");

        await store.DeleteProfileAsync("Rig2");

        Assert.Single(store.ProfileNames);
        Assert.Equal("Default", store.ActiveProfile.Name);
    }

    [Fact]
    public async Task DeleteProfileAsync_DeletingActiveProfile_SwitchesToAnotherRemainingProfile()
    {
        var store = await NewLoadedStoreAsync();
        await store.CreateProfileAsync("Rig2"); // now active

        await store.DeleteProfileAsync("Rig2");

        Assert.Single(store.ProfileNames);
        Assert.Equal("Default", store.ActiveProfile.Name);
    }

    [Fact]
    public async Task DeleteProfileAsync_RefusesToDeleteTheOnlyRemainingProfile()
    {
        var store = await NewLoadedStoreAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteProfileAsync("Default"));
        Assert.Single(store.ProfileNames);
    }

    [Fact]
    public async Task ActiveProfileChanged_FiresOnSwitchCreateAndDelete()
    {
        var store = await NewLoadedStoreAsync();
        int fireCount = 0;
        store.ActiveProfileChanged += () => fireCount++;

        await store.CreateProfileAsync("Rig2"); // fires (switch to new)
        await store.SwitchActiveProfileAsync("Default"); // fires
        await store.DeleteProfileAsync("Rig2"); // does not touch active profile - should NOT fire

        Assert.Equal(2, fireCount);
    }
}
