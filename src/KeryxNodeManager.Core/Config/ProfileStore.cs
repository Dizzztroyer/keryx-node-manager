using KeryxNodeManager.Core.Localization;
using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.Config;

/// <summary>
/// Thin orchestration layer over ConfigStore that keeps one MiningProfile "active" in memory for
/// the UI to bind against directly (Node/Miner/Dashboard all share the same ProfileStore
/// instance via DI). This is what turns the previously-decorative Dashboard Start/Stop buttons
/// into something backed by a real, persisted profile - see PROJECT_STATUS.md "MiningProfile
/// end-to-end wiring".
/// </summary>
public sealed class ProfileStore
{
    private readonly ConfigStore _configStore;
    private AppSettings _settings = new();

    public ProfileStore(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public MiningProfile ActiveProfile { get; private set; } = new();

    public AppSettings Settings => _settings;

    /// <summary>Profile names in persisted order - what the Profiles page's list/dropdown binds
    /// to. A live projection, not a cached snapshot, so it can never go stale relative to
    /// Settings.Profiles.</summary>
    public IReadOnlyList<string> ProfileNames => _settings.Profiles.Select(p => p.Name).ToList();

    /// <summary>Full profile objects in persisted order - used by the Profiles page's per-row
    /// quick-glance summary (mining address/GPU count), which needs more than just the name.
    /// Same "live projection, never a stale cached snapshot" rule as ProfileNames above.</summary>
    public IReadOnlyList<MiningProfile> Profiles => _settings.Profiles;

    /// <summary>Raised whenever ActiveProfile changes (load, switch, create, or the
    /// active profile being the one deleted) - lets MainViewModel's nav-strip
    /// "Профиль: X" label stay live without polling.</summary>
    public event Action? ActiveProfileChanged;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        _settings = await _configStore.LoadAsync(ct);
        if (_settings.Profiles.Count == 0)
        {
            _settings.Profiles.Add(new MiningProfile());
        }
        SetActiveProfile(_settings.Profiles.Find(p => p.Name == _settings.ActiveProfileName)
                        ?? _settings.Profiles[0]);
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (!_settings.Profiles.Contains(ActiveProfile))
        {
            _settings.Profiles.Add(ActiveProfile);
        }
        _settings.ActiveProfileName = ActiveProfile.Name;
        await _configStore.SaveAtomicAsync(_settings, ct);
    }

    /// <summary>Makes an already-existing profile (by name) the active one and persists the
    /// choice. Node/Miner/Dashboard/GPU pages all bind to the same ProfileStore instance, so this
    /// is the one place "switch profile" needs to happen for every page to see the new data.</summary>
    public Task SwitchActiveProfileAsync(string name, CancellationToken ct = default)
    {
        var target = _settings.Profiles.Find(p => p.Name == name)
            ?? throw new InvalidOperationException(CoreStrings.Format("Profile.NotFound", name));
        SetActiveProfile(target);
        return SaveAsync(ct);
    }

    /// <summary>Creates a new, blank-defaults profile and switches to it immediately (a freshly
    /// created profile is presumably the one the user wants to start configuring, matching how
    /// the wizard's own "start from a clean profile" flow behaves).</summary>
    public Task CreateProfileAsync(string name, CancellationToken ct = default)
    {
        name = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(CoreStrings.Get("Profile.NameEmpty"), nameof(name));
        }
        if (_settings.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(CoreStrings.Format("Profile.AlreadyExists", name));
        }

        var profile = new MiningProfile { Name = name };
        _settings.Profiles.Add(profile);
        SetActiveProfile(profile);
        return SaveAsync(ct);
    }

    /// <summary>Renames an existing profile in place (same MiningProfile instance, GPU
    /// assignments/executable paths/etc. all carry over - a rename is not a create+delete).</summary>
    public Task RenameProfileAsync(string oldName, string newName, CancellationToken ct = default)
    {
        newName = newName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException(CoreStrings.Get("Profile.NameEmpty"), nameof(newName));
        }

        var target = _settings.Profiles.Find(p => p.Name == oldName)
            ?? throw new InvalidOperationException(CoreStrings.Format("Profile.NotFound", oldName));

        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) &&
            _settings.Profiles.Any(p => string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(CoreStrings.Format("Profile.AlreadyExists", newName));
        }

        target.Name = newName;
        if (ReferenceEquals(target, ActiveProfile))
        {
            ActiveProfileChanged?.Invoke();
        }
        return SaveAsync(ct);
    }

    /// <summary>Deletes a profile. Refuses to delete the last remaining profile - the app always
    /// needs at least one MiningProfile to bind Node/Miner/Dashboard against (matching LoadAsync's
    /// own auto-create-a-default behaviour for an empty list). Deleting the currently active
    /// profile switches to whichever profile is now first in the list rather than leaving
    /// ActiveProfile pointing at a MiningProfile instance no longer present in Settings.Profiles.</summary>
    public Task DeleteProfileAsync(string name, CancellationToken ct = default)
    {
        if (_settings.Profiles.Count <= 1)
        {
            throw new InvalidOperationException(CoreStrings.Get("Profile.CannotDeleteLast"));
        }

        var target = _settings.Profiles.Find(p => p.Name == name)
            ?? throw new InvalidOperationException(CoreStrings.Format("Profile.NotFound", name));

        _settings.Profiles.Remove(target);
        if (ReferenceEquals(target, ActiveProfile))
        {
            SetActiveProfile(_settings.Profiles[0]);
        }
        return SaveAsync(ct);
    }

    private void SetActiveProfile(MiningProfile profile)
    {
        ActiveProfile = profile;
        ActiveProfileChanged?.Invoke();
    }
}
