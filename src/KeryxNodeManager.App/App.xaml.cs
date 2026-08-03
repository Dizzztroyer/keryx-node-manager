using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using KeryxNodeManager.App.SingleInstance;
using KeryxNodeManager.App.Tray;
using KeryxNodeManager.App.ViewModels;
using KeryxNodeManager.App.Views;
using KeryxNodeManager.Core.Autostart;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.Logging;
using KeryxNodeManager.Core.ModelAssignment;
using KeryxNodeManager.Core.Runtime;
using KeryxNodeManager.Core.Safety;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeryxNodeManager.App;

public partial class App : Application
{
    // Single-instance guard (brief §10: re-launching the app should surface the existing window,
    // not start a second copy managing the same node/miner processes).
    private static Mutex? _singleInstanceMutex;
    private IHost? _host;
    private TrayIconService? _tray;
    private SingleInstanceIpc? _singleInstanceIpc;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "KeryxNodeManager.SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Real single-instance IPC (brief §10/§27): try to signal the already-running
            // instance to bring its window to front before giving up.
            //
            // Real bug found and fixed here (third occurrence of the same class in this project -
            // see the identical writeup on ProfileStore.LoadAsync below): my original comment here
            // claimed the bare `.GetAwaiter().GetResult()` was safe because "no WPF window/Dispatcher
            // machinery has been touched yet." That reasoning was wrong - WPF installs the
            // DispatcherSynchronizationContext on the UI thread as part of Application's own startup
            // sequence, before OnStartup ever runs, regardless of what this method does. Confirmed
            // live: launching a second instance while the first was running left the second process
            // alive (Responding=True) with MainWindowHandle=0, hung indefinitely - the exact "zombie,
            // no window" signature from the ProfileStore.LoadAsync bug. Here it is deterministic
            // rather than intermittent, because NamedPipeClientStream.ConnectAsync is real
            // wait-for-a-server I/O that essentially never completes synchronously (unlike a small
            // local file read, which sometimes does). Task.Run moves the whole async chain onto a
            // thread-pool thread with no captured DispatcherSynchronizationContext, so it can't
            // deadlock this way.
            bool signaled = Task.Run(() => SingleInstanceIpc.TrySendShowRequestAsync(TimeSpan.FromSeconds(2)))
                .GetAwaiter().GetResult();
            if (!signaled)
            {
                // Only shown if the existing instance couldn't be reached at all (e.g. a narrow
                // startup race, or something is actually wrong) - the honest fallback this
                // message box always was, not the primary path anymore.
                MessageBox.Show(
                    "Keryx Node Manager уже запущен, но не удалось связаться с ним. Проверьте системный трей.",
                    "Keryx Node Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        bool useMock = e.Args.Contains("--mock");

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeryxNodeManager");
        Directory.CreateDirectory(appDataDir);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new ConfigStore(Path.Combine(appDataDir, "settings.json")));
                services.AddSingleton<ProfileStore>();
                services.AddSingleton<TierAssigner>();
                // Factory (not `new LogSink(...)`) because the byte/day thresholds come from
                // AppSettings, which isn't loaded from disk until ProfileStore.LoadAsync() runs
                // just below - resolving this lazily on first use (after that load) means it
                // always sees the real, loaded settings rather than AppSettings' bare defaults.
                services.AddSingleton(sp =>
                {
                    var settings = sp.GetRequiredService<ProfileStore>().Settings;
                    return LogSink.FromMegabytes(
                        Path.Combine(appDataDir, "Logs"), settings.LogRetentionDays, settings.MaxLogSizeMb);
                });
                // Shared across all model downloads on the Models page - HttpClient is designed
                // to be reused as a singleton (a new one per request exhausts sockets under load).
                services.AddSingleton(new HttpClient());
                services.AddSingleton<TaskSchedulerAutostart>();
                services.AddSingleton<SafetyMonitor>();

                if (useMock)
                {
                    services.AddSingleton<IGpuInfoProvider, MockGpuInfoProvider>();
                    services.AddSingleton<IKeryxRuntimeBackend, MockRuntimeBackend>();
                    // Same reasoning as MockGpuInfoProvider: --mock runs must never touch a real
                    // driver API, and the mock's fixed ranges (see MockGpuOverclockController's own
                    // doc comment) are what MockGpuOverclockControllerTests already exercises.
                    services.AddSingleton<IGpuOverclockController, MockGpuOverclockController>();
                }
                else
                {
                    services.AddSingleton<IGpuInfoProvider>(new NvidiaSmiGpuInfoProvider());
                    services.AddSingleton<IKeryxRuntimeBackend, NativeWindowsRuntimeBackend>();
                    // Windows-only, NVAPI-backed - see NvApiGpuOverclockController's own doc
                    // comment for why this can only ever live in KeryxNodeManager.App.
                    services.AddSingleton<IGpuOverclockController, KeryxNodeManager.App.Gpu.NvApiGpuOverclockController>();
                }

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<GpuViewModel>();
                services.AddSingleton<NodeViewModel>();
                services.AddSingleton<MinerViewModel>();
                services.AddSingleton<ModelsViewModel>();
                services.AddSingleton<LogsViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<ProfilesViewModel>();
                services.AddSingleton<AboutViewModel>();
                services.AddSingleton<MainWindow>();
                // Transient, not singleton: the wizard is shown at most once per launch and must
                // not linger in memory holding stale Checks/GpuPreview collections afterwards.
                services.AddTransient<WizardViewModel>();
                services.AddTransient<WizardWindow>();
            })
            .Build();

        // Loaded synchronously before the window shows: settings.json is a small local file, and
        // every page (Dashboard/Node/Miner) needs the same loaded ProfileStore.ActiveProfile
        // instance to already exist by the time its ViewModel is constructed.
        //
        // Real bug found and fixed here: this used to be a bare
        // `profileStore.LoadAsync().GetAwaiter().GetResult()`. WPF installs a
        // DispatcherSynchronizationContext on the UI thread before OnStartup runs, but the
        // Dispatcher's actual message pump (Dispatcher.Run()) doesn't start until AFTER OnStartup
        // returns. ConfigStore.LoadAsync does real async file I/O (File.OpenRead +
        // JsonDocument.ParseAsync); whenever that I/O doesn't complete synchronously, its
        // continuation is posted back to the captured DispatcherSynchronizationContext - which
        // has no pump running yet to deliver it - while this very thread sits blocked inside
        // GetResult() waiting for that same continuation. Deadlock, forever, with no exception
        // and no window ever created (reproduced live: 9 consecutive launches hung indefinitely
        // with MainWindowHandle=0 and only an invisible message-only window/IME window created,
        // confirmed via a temporary trace log that execution never got past this line). It is
        // intermittent, not deterministic - whether the FileStream read happens to complete
        // synchronously depends on I/O timing/contention - which is almost certainly the real
        // explanation for the "zombie process, no window" launch flake documented across several
        // earlier sessions and previously blamed on unrelated background AV/installer activity.
        // Task.Run moves the whole async chain onto a thread-pool thread with no
        // DispatcherSynchronizationContext installed, so its awaits complete against the
        // thread-pool's own context and never need the UI dispatcher to pump - GetResult() here
        // just blocks on an independently-progressing Task, which cannot deadlock this way.
        var profileStore = _host.Services.GetRequiredService<ProfileStore>();
        Task.Run(() => profileStore.LoadAsync()).GetAwaiter().GetResult();

        // Brief §16 localization: apply the persisted language choice before any page is
        // constructed, so nothing briefly flashes in the wrong language on startup.
        LocalizationManager.Apply(profileStore.Settings.Language);

        // First-run wizard (brief §4): shown once, before MainWindow, until FirstRunCompleted is
        // set (by Finish or Skip - both persist, see WizardViewModel.FinishAsync). Also shown in
        // --mock runs deliberately (not special-cased) - a fresh settings.json is a fresh
        // settings.json either way, and gating this on the launch mode would mean the wizard path
        // is never covered by the same manual verification the rest of the app gets in --mock.
        if (!profileStore.Settings.FirstRunCompleted)
        {
            var wizard = _host.Services.GetRequiredService<WizardWindow>();
            wizard.ShowDialog();
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        // Resolved before constructing TrayIconService (rather than after, as a prior pass had
        // it) because the tray menu's Start All/Stop All/Exit-with-stop items now call straight
        // into these same two singletons - see TrayIconService's own doc comment.
        var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
        var dashboardViewModel = _host.Services.GetRequiredService<DashboardViewModel>();
        _tray = new TrayIconService(mainWindow, mainViewModel, dashboardViewModel, ShutdownWithCleanup);
        mainWindow.Show();

        // Brief §10 tray state colors: drive the tray icon's badge from the same
        // DashboardViewModel state the Dashboard page itself displays, rather than inventing a
        // second status-tracking path. Only Stopped/Starting/Running are wired here because those
        // are the only states this app can currently observe honestly - NodeStatus/MinerStatus
        // (set from ProcessSupervisor.EventRaised) plus StartAllCommand.IsRunning (CommunityToolkit's
        // generated AsyncRelayCommand property, true for the moment between "Запустить всё" being
        // clicked and the launch attempt finishing) cover that. TrayState.Error/InferenceActive
        // still exist and SetState already renders their icon correctly, but nothing calls them yet:
        // there is no Core-layer "last launch failed" or "inference in progress" signal today (see
        // PROJECT_STATUS.md) - wiring those honestly would need that signal added first, not a
        // guess grafted onto this pass.
        void UpdateTrayState() => Dispatcher.Invoke(() =>
        {
            var state = dashboardViewModel.StartAllCommand.IsRunning
                ? TrayState.Starting
                : (dashboardViewModel.NodeStatus == "Работает" || dashboardViewModel.MinerStatus == "Работает"
                    ? TrayState.Running
                    : TrayState.Stopped);
            _tray?.SetState(state);
        });
        dashboardViewModel.PropertyChanged += (_, _) => UpdateTrayState();
        dashboardViewModel.StartAllCommand.PropertyChanged += (_, _) => UpdateTrayState();
        UpdateTrayState();

        // Start listening for "SHOW" signals from any future second launch attempt. Started only
        // now (after this instance has definitely won the mutex race and has a real window/tray
        // to restore) - the callback runs on a background thread pumped by NamedPipeServerStream's
        // async accept loop, so it must marshal back onto the UI thread via the Dispatcher before
        // touching the window, same as every other cross-thread callback in this app
        // (ProcessSupervisor.EventRaised, SafetyMonitor.EventRaised, LogSink.LineAppended).
        _singleInstanceIpc = new SingleInstanceIpc();
        _singleInstanceIpc.StartServer(() => Dispatcher.Invoke(() => _tray?.ShowMainWindow()));
    }

    private void ShutdownWithCleanup()
    {
        _singleInstanceIpc?.Dispose();
        _tray?.Dispose();
        _host?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceIpc?.Dispose();
        _tray?.Dispose();
        _host?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
