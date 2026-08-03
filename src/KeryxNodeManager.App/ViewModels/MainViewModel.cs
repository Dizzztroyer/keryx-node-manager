using CommunityToolkit.Mvvm.ComponentModel;
using KeryxNodeManager.Core.Config;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Hosts the left-nav selection (brief §27: Dashboard/GPU/Models/Node/Miner/Logs/
/// Diagnostics/Profiles/Settings/About) and the overall status strip shown at the bottom of the
/// nav. Most pages are now real, working pages - Diagnostics remains a placeholder
/// (see PROJECT_STATUS.md for the exact split).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore;

    [ObservableProperty]
    private string _selectedPage = "Dashboard";

    [ObservableProperty]
    private string _activeProfileName = "Default";

    public string AppVersion => AppVersionInfo.Current;

    public IReadOnlyList<string> Pages { get; } = new[]
    {
        "Dashboard", "GPU", "Models", "Node", "Miner", "Logs", "Diagnostics", "Profiles", "Settings", "About",
    };

    public MainViewModel(ProfileStore profileStore)
    {
        _profileStore = profileStore;
        // ActiveProfile is already loaded by the time App.xaml.cs constructs MainWindow (see
        // App.OnStartup), so this reads real state immediately rather than the previous
        // hardcoded "Default" literal that never actually reflected ProfileStore.
        _activeProfileName = _profileStore.ActiveProfile.Name;
        _profileStore.ActiveProfileChanged += () => ActiveProfileName = _profileStore.ActiveProfile.Name;
    }
}
