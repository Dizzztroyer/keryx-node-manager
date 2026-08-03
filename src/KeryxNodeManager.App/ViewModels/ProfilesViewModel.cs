using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Secrets;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>One row in the Profiles page's list: the name (used for selection/switch/rename/delete,
/// unchanged from before this increment) plus a one-line quick-glance summary (PROJECT_STATUS.md
/// "In progress" item 9) - previously the list showed names only, giving no way to tell profiles
/// apart at a glance without switching to each one and checking the Miner/GPU pages. The mining
/// address is masked via the same SecretMasker.MaskAddress DiagnosticsExporter already uses for
/// redacting addresses in exported diagnostic ZIPs, rather than showing the full address in a
/// plain list.</summary>
public sealed record ProfileRow(string Name, string Summary);

/// <summary>
/// Drives the Profiles page: switch/create/rename/delete named MiningProfiles. AppSettings has
/// supported a Profiles list + ActiveProfileName since the very first config schema, but until
/// this increment ProfileStore itself could only load-once/save-the-one-it-has - there was no way
/// to actually reach a second profile from the UI. This ViewModel is a thin wrapper over the new
/// ProfileStore.Switch/Create/Rename/DeleteAsync methods; it holds no profile data of its own, so
/// it can never disagree with what Node/Miner/Dashboard/GPU are actually bound to.
/// </summary>
public partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore;

    public ObservableCollection<ProfileRow> ProfileRows { get; } = new();

    [ObservableProperty]
    private string? _selectedProfileName;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _renameToName = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public string ActiveProfileName => _profileStore.ActiveProfile.Name;

    public ProfilesViewModel(ProfileStore profileStore)
    {
        _profileStore = profileStore;
        RefreshList();
        SelectedProfileName = _profileStore.ActiveProfile.Name;
        _profileStore.ActiveProfileChanged += () => OnPropertyChanged(nameof(ActiveProfileName));
    }

    private void RefreshList()
    {
        ProfileRows.Clear();
        foreach (var profile in _profileStore.Profiles)
        {
            var addressPart = string.IsNullOrWhiteSpace(profile.MiningAddress)
                ? "адрес не задан"
                : SecretMasker.MaskAddress(profile.MiningAddress);
            var gpuCount = profile.GpuAssignments.Count;
            var gpuPart = gpuCount == 0
                ? "GPU: авто"
                : $"GPU назначено: {gpuCount}";
            ProfileRows.Add(new ProfileRow(profile.Name, $"{addressPart} · {gpuPart}"));
        }
    }

    partial void OnSelectedProfileNameChanged(string? value)
    {
        RenameToName = value ?? string.Empty;
    }

    [RelayCommand]
    private async Task SwitchAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileName)) return;
        try
        {
            await _profileStore.SwitchActiveProfileAsync(SelectedProfileName);
            OnPropertyChanged(nameof(ActiveProfileName));
            StatusMessage = $"Активный профиль: «{SelectedProfileName}».";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось переключить профиль: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        try
        {
            await _profileStore.CreateProfileAsync(NewProfileName);
            var created = NewProfileName.Trim();
            RefreshList();
            SelectedProfileName = created;
            NewProfileName = string.Empty;
            OnPropertyChanged(nameof(ActiveProfileName));
            StatusMessage = $"Профиль «{created}» создан и стал активным.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось создать профиль: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileName)) return;
        try
        {
            var oldName = SelectedProfileName;
            await _profileStore.RenameProfileAsync(oldName, RenameToName);
            var newName = RenameToName.Trim();
            RefreshList();
            SelectedProfileName = newName;
            OnPropertyChanged(nameof(ActiveProfileName));
            StatusMessage = $"Профиль «{oldName}» переименован в «{newName}».";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось переименовать профиль: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileName)) return;
        try
        {
            var deleted = SelectedProfileName;
            await _profileStore.DeleteProfileAsync(deleted);
            RefreshList();
            SelectedProfileName = _profileStore.ActiveProfile.Name;
            OnPropertyChanged(nameof(ActiveProfileName));
            StatusMessage = $"Профиль «{deleted}» удалён.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось удалить профиль: {ex.Message}";
        }
    }
}
