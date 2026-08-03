using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using KeryxNodeManager.App.ViewModels;

namespace KeryxNodeManager.App.Tray;

public enum TrayState { Stopped, Starting, Running, Error, InferenceActive }

/// <summary>
/// Brief §10: tray icon with a color per overall state, context menu with the standard actions,
/// and a three-way "Выйти" confirmation (close UI only / stop everything / cancel). Start
/// All/Stop All/Exit-with-stop all call straight through to DashboardViewModel's own commands -
/// the same ones the Dashboard page's buttons use - so tray and page can never disagree about what
/// "start" or "stop" means. Per-process restart (Node/Miner individually) is still a documented
/// gap, not wired - see PROJECT_STATUS.md.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _mainViewModel;
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly Action _exitApplication;

    /// <summary>One colored-badge .ico variant per TrayState (brief §10: grey/yellow/green/red/
    /// blue), all sharing the same base glyph as the original tray.ico so the icon still reads as
    /// "Keryx" at a glance and only the badge color changes. Loaded once and cached rather than
    /// constructing a new BitmapImage on every SetState call.</summary>
    private static readonly IReadOnlyDictionary<TrayState, System.Windows.Media.Imaging.BitmapImage> IconsByState =
        new Dictionary<TrayState, System.Windows.Media.Imaging.BitmapImage>
        {
            [TrayState.Stopped] = LoadIcon("tray-stopped.ico"),
            [TrayState.Starting] = LoadIcon("tray-starting.ico"),
            [TrayState.Running] = LoadIcon("tray-running.ico"),
            [TrayState.Error] = LoadIcon("tray-error.ico"),
            [TrayState.InferenceActive] = LoadIcon("tray-inference.ico"),
        };

    private static System.Windows.Media.Imaging.BitmapImage LoadIcon(string fileName) =>
        new(new Uri($"pack://application:,,,/Resources/{fileName}"));

    public TrayIconService(
        MainWindow mainWindow, MainViewModel mainViewModel, DashboardViewModel dashboardViewModel,
        Action exitApplication)
    {
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;
        _dashboardViewModel = dashboardViewModel;
        _exitApplication = exitApplication;

        _icon = new TaskbarIcon
        {
            ToolTipText = "Keryx Node Manager",
            IconSource = IconsByState[TrayState.Stopped],
        };
        _icon.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        _icon.ContextMenu = BuildContextMenu();
        SetState(TrayState.Stopped);
    }

    public void SetState(TrayState state)
    {
        _icon.ToolTipText = state switch
        {
            TrayState.Stopped => AppStrings.Get("Str_Tray_Tooltip_Stopped"),
            TrayState.Starting => AppStrings.Get("Str_Tray_Tooltip_Starting"),
            TrayState.Running => AppStrings.Get("Str_Tray_Tooltip_Running"),
            TrayState.Error => AppStrings.Get("Str_Tray_Tooltip_Error"),
            TrayState.InferenceActive => AppStrings.Get("Str_Tray_Tooltip_InferenceActive"),
            _ => "Keryx Node Manager",
        };
        _icon.IconSource = IconsByState[state];
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        void AddItem(string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        AddItem(AppStrings.Get("Str_Tray_Menu_Open"), ShowMainWindow);
        menu.Items.Add(new System.Windows.Controls.Separator());
        // Reuse the exact same commands the Dashboard page's own buttons call - these were left as
        // no-op lambdas with a stale "wired once a MiningProfile exists" comment from before
        // ProfileStore/DashboardViewModel's commands became real; that condition has been true
        // since the GPU-wiring session, this was just never revisited (PROJECT_STATUS.md).
        // Fire-and-forget is intentional here (same as a real button click) - IAsyncRelayCommand
        // handles its own IsRunning/re-entrancy guarding.
        AddItem(AppStrings.Get("Str_Dashboard_StartAll"), () => _dashboardViewModel.StartAllCommand.Execute(null));
        AddItem(AppStrings.Get("Str_Dashboard_StopAll"), () => _dashboardViewModel.StopAllCommand.Execute(null));
        // Restarting node/miner individually (as opposed to Stop All + Start All together) has no
        // single Core-layer primitive yet - ProcessSupervisor only exposes Start*/Stop*, and each
        // Start*Async needs a freshly-built LaunchSpec (arguments, GPU assignment resolution, env
        // vars) that today only DashboardViewModel.StartAllAsync knows how to assemble per-process.
        // Left as an honest gap rather than wiring a "stop everything, start everything" fake
        // restart under a per-process-restart label - documented in PROJECT_STATUS.md.
        AddItem(AppStrings.Get("Str_Tray_Menu_RestartMiner"), () => { });
        AddItem(AppStrings.Get("Str_Tray_Menu_RestartNode"), () => { });
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddItem(AppStrings.Get("Str_Tray_Menu_OpenLogs"), () =>
        {
            _mainViewModel.SelectedPage = "Logs";
            ShowMainWindow();
        });
        AddItem(AppStrings.Get("Str_Settings_Title"), () =>
        {
            _mainViewModel.SelectedPage = "Settings";
            ShowMainWindow();
        });
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddItem(AppStrings.Get("Str_Tray_Menu_Exit"), ConfirmExit);

        return menu;
    }

    /// <summary>Public so App's single-instance IPC server can reuse the exact same
    /// restore-from-tray-or-minimized behaviour when a second launch attempt signals the already-
    /// running instance to come to front (brief §10) - one restore implementation, not two.</summary>
    public void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private async void ConfirmExit()
    {
        var result = MessageBox.Show(
            AppStrings.Get("Str_Tray_ExitConfirm_Body"),
            AppStrings.Get("Str_Tray_ExitConfirm_Title"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                // Previously a TODO: this branch's dialog text promises to stop node/miner first,
                // but nothing actually called ProcessSupervisor.StopAsync - it just force-closed
                // exactly like "Нет" did, silently breaking the promise the dialog just made.
                // DashboardViewModel.StopAllCommand already does the real stop (both supervisors +
                // SafetyMonitor) - awaited here so the app doesn't exit mid-stop.
                await _dashboardViewModel.StopAllCommand.ExecuteAsync(null);
                _mainWindow.ForceClose();
                _exitApplication();
                break;
            case MessageBoxResult.No:
                _mainWindow.ForceClose();
                _exitApplication();
                break;
            case MessageBoxResult.Cancel:
            default:
                break;
        }
    }

    public void Dispose() => _icon.Dispose();
}
