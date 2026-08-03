using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Autostart;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Real Settings page (brief §11 autostart, plus the handful of AppSettings fields - log
/// retention/size, monitoring interval, tray-close behavior, notifications - that have existed on
/// AppSettings since earlier sessions but had no UI to change them). Binds directly to
/// ProfileStore.Settings, matching NodeViewModel/MinerViewModel's "single-writer settings screen,
/// plain POCO binding" pattern - most fields are saved via an explicit "Сохранить" button.
///
/// StartWithWindows is the one field here that is NOT "just persist the bool": a checked checkbox
/// must correspond to a real Task Scheduler entry, or the setting would lie about what actually
/// happens at next Windows logon. So toggling it calls TaskSchedulerAutostart.RegisterAsync/
/// UnregisterAsync immediately (same "persist on change, not on a separate Save" reasoning as the
/// GPU page's mode dropdown), and the checkbox's initial state is read back from the real Task
/// Scheduler on construction rather than assumed from the persisted flag - a hand-edited
/// settings.json, or the exe having moved since the task was registered, must not silently show a
/// checked box that doesn't reflect reality.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore;
    private readonly TaskSchedulerAutostart _autostart;
    private bool _suppressAutostartHandling;

    public AppSettings Settings => _profileStore.Settings;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _isCheckingAutostart = true;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Brief §16 localization, first increment - see LocalizationManager's doc comment
    /// for the exact scope (Settings page + nav footer only, so far). Backed by the
    /// previously-dead AppSettings.Language field.</summary>
    [ObservableProperty]
    private string _language = "ru";

    public SettingsViewModel(ProfileStore profileStore, TaskSchedulerAutostart autostart)
    {
        _profileStore = profileStore;
        _autostart = autostart;
        _language = Settings.Language;
        _ = InitializeAutostartStateAsync();
    }

    partial void OnLanguageChanged(string value)
    {
        LocalizationManager.Apply(value);
        Settings.Language = value;
        _ = _profileStore.SaveAsync();
    }

    private async Task InitializeAutostartStateAsync()
    {
        try
        {
            bool actuallyRegistered = await _autostart.IsRegisteredAsync();
            _suppressAutostartHandling = true;
            StartWithWindows = actuallyRegistered;
            _suppressAutostartHandling = false;

            if (actuallyRegistered != Settings.StartWithWindows)
            {
                // Reality and the persisted flag disagree - trust the real Task Scheduler state
                // and correct settings.json rather than continue reporting a stale value.
                Settings.StartWithWindows = actuallyRegistered;
                await _profileStore.SaveAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = AppStrings.Format("Str_Settings_CheckAutostartFailed", ex.Message);
        }
        finally
        {
            IsCheckingAutostart = false;
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressAutostartHandling) return;
        _ = ApplyAutostartToggleAsync(value);
    }

    private async Task ApplyAutostartToggleAsync(bool enable)
    {
        try
        {
            if (enable)
            {
                string exePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException(AppStrings.Get("Str_Settings_ExePathNotFound"));
                await _autostart.RegisterAsync(exePath);
                StatusMessage = AppStrings.Get("Str_Settings_AutostartEnabled");
            }
            else
            {
                await _autostart.UnregisterAsync();
                StatusMessage = AppStrings.Get("Str_Settings_AutostartDisabled");
            }
            Settings.StartWithWindows = enable;
            await _profileStore.SaveAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = AppStrings.Format("Str_Settings_AutostartChangeFailed", ex.Message);
            // The Task Scheduler call failed - revert the checkbox so it doesn't show a state
            // that was never actually applied.
            _suppressAutostartHandling = true;
            StartWithWindows = !enable;
            _suppressAutostartHandling = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _profileStore.SaveAsync();
        StatusMessage = AppStrings.Get("Str_Settings_SavedGeneric");
    }
}
