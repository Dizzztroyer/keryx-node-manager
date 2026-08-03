using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using KeryxNodeManager.App.ViewModels;
using KeryxNodeManager.App.Views;

namespace KeryxNodeManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly GpuViewModel _gpuViewModel;
    private readonly NodeViewModel _nodeViewModel;
    private readonly MinerViewModel _minerViewModel;
    private readonly ModelsViewModel _modelsViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ProfilesViewModel _profilesViewModel;
    private readonly AboutViewModel _aboutViewModel;
    private bool _forceClose;

    public MainWindow(
        MainViewModel viewModel,
        DashboardViewModel dashboardViewModel,
        GpuViewModel gpuViewModel,
        NodeViewModel nodeViewModel,
        MinerViewModel minerViewModel,
        ModelsViewModel modelsViewModel,
        LogsViewModel logsViewModel,
        SettingsViewModel settingsViewModel,
        ProfilesViewModel profilesViewModel,
        AboutViewModel aboutViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _dashboardViewModel = dashboardViewModel;
        _gpuViewModel = gpuViewModel;
        _nodeViewModel = nodeViewModel;
        _minerViewModel = minerViewModel;
        _modelsViewModel = modelsViewModel;
        _logsViewModel = logsViewModel;
        _settingsViewModel = settingsViewModel;
        _profilesViewModel = profilesViewModel;
        _aboutViewModel = aboutViewModel;
        DataContext = _viewModel;

        // Dashboard's "Перейти к настройкам" nudge button (PROJECT_STATUS.md "Known issues": a
        // missing NodeExecutablePath/MiningAddress used to be an inert LastActionMessage string
        // with no way to act on it) - Dashboard raises the page name, MainWindow (which already
        // owns both MainViewModel and the nav ListBox) does the actual switch. Setting SelectedPage
        // updates NavList.SelectedItem via its TwoWay binding, which fires NavList_SelectionChanged
        // exactly as if the user had clicked the nav item themselves.
        _dashboardViewModel.NavigationRequested += page => _viewModel.SelectedPage = page;

        Loaded += (_, _) => ShowPage(_viewModel.SelectedPage);
    }

    /// <summary>Called by App/TrayIconService when the user picks "Выйти" from the tray menu and
    /// confirms it - bypasses the minimize-to-tray behaviour in Window_Closing.</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is string page) ShowPage(page);
    }

    private void ShowPage(string page)
    {
        PageHost.Content = page switch
        {
            "Dashboard" => new DashboardView { DataContext = _dashboardViewModel },
            "GPU" => new GpuView { DataContext = _gpuViewModel },
            "Node" => new NodeView { DataContext = _nodeViewModel },
            "Miner" => new MinerView { DataContext = _minerViewModel },
            "Models" => new ModelsView { DataContext = _modelsViewModel },
            "Logs" => new LogsView { DataContext = _logsViewModel },
            "Settings" => new SettingsView { DataContext = _settingsViewModel },
            "Profiles" => new ProfilesView { DataContext = _profilesViewModel },
            "About" => new AboutView { DataContext = _aboutViewModel },
            _ => new PlaceholderView(page),
        };
    }

    /// <summary>Brief §10: closing the main window minimizes to tray by default instead of
    /// stopping the node/miner. TrayIconService's "Выйти" menu item calls ForceClose() after the
    /// user confirms via its own dialog, which sets _forceClose so this handler lets it through.</summary>
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        Hide();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // Placeholder hook: a "start minimized to tray" setting would call Hide() here on the
        // first StateChanged after Minimized once Settings page persists that toggle
        // (see PROJECT_STATUS.md).
    }

    // Same pattern as AboutView.xaml.cs's Hyperlink_RequestNavigate - added here too so the nav
    // footer's official-links row (brief follow-up, 2026-08-03: user asked for Discord/X/
    // Telegram/GitHub visible "somewhere at the bottom" of the app, not buried on the About page)
    // works without every page needing its own copy.
    private void SocialLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
