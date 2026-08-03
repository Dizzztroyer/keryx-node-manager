using CommunityToolkit.Mvvm.ComponentModel;
using KeryxNodeManager.Core.Config;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Hosts the left-nav selection (brief §27: Dashboard/GPU/Models/Node/Miner/Logs/Settings/About)
/// and the overall status strip shown at the bottom of the nav.
///
/// Profiles and Diagnostics are deliberately excluded from <see cref="Pages"/> (0.2.6 brief §6/§7):
/// neither brings enough end-user value yet to show in the normal nav (Diagnostics is an empty
/// placeholder; Profiles exposes multi-profile management most users don't need, since a single
/// implicit "Default" profile - already how ProfileStore behaves for anyone who never opens that
/// page - covers the common case). Their ViewModels/Views/routing in MainWindow.xaml.cs are left
/// entirely intact, only unreachable via the normal UI, so existing profile data, config
/// migrations, and the Diagnostics scaffolding are not touched - see PROJECT_STATUS.md.
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
        "Dashboard", "GPU", "Models", "Node", "Miner", "Logs", "Settings", "About",
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
