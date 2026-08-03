using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Cli;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.Logging;
using KeryxNodeManager.Core.ModelAssignment;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.Process;
using KeryxNodeManager.Core.Runtime;
using KeryxNodeManager.Core.Safety;
using KeryxNodeManager.Core.Localization;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Overview page (brief §5): node/miner/network status, quick actions, GPU count. Start
/// All/Stop All now actually launch keryxd/keryx-miner (or their mock equivalents) through
/// ProcessSupervisor, built from the profile the Node/Miner settings pages persist via
/// ProfileStore - this is the "MiningProfile end-to-end wiring" tracked in PROJECT_STATUS.md.
/// Hashrate/earnings are deliberately absent: docs/KERYX_RESEARCH.md §4 confirms the miner
/// reports hashrate and block counts only, never a currency estimate, so this dashboard never
/// invents one.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly IKeryxRuntimeBackend _runtimeBackend;
    private readonly IGpuInfoProvider _gpuInfoProvider;
    private readonly TierAssigner _tierAssigner;
    private readonly ProfileStore _profileStore;
    private readonly LogSink _logSink;
    private readonly SafetyMonitor _safetyMonitor;
    private readonly ProcessSupervisor _nodeSupervisor;
    private readonly ProcessSupervisor _minerSupervisor;

    [ObservableProperty]
    private string _nodeStatus = "";

    [ObservableProperty]
    private string _minerStatus = "";

    [ObservableProperty]
    private int _activeGpuCount;

    [ObservableProperty]
    private int _disabledGpuCount;

    [ObservableProperty]
    private string? _lastActionMessage;

    /// <summary>Which nav page the "Перейти к настройкам" nudge button should jump to, or null to
    /// hide it. Set alongside LastActionMessage in StartAllAsync's two config-validation branches
    /// (PROJECT_STATUS.md "Known issues": these errors were previously just inert text with no way
    /// to act on them directly from the Dashboard) - cleared on every other path so the button only
    /// ever appears for the specific "go fix this" case it was built for, not lingering after an
    /// unrelated status update.</summary>
    [ObservableProperty]
    private string? _missingConfigTarget;

    /// <summary>Raised when the user clicks the "Перейти к настройкам" nudge button.
    /// DashboardViewModel has no reference to MainViewModel/MainWindow (and shouldn't - Dashboard
    /// is a page, not the nav shell), so navigation is requested via this event and MainWindow.xaml.cs
    /// (which already owns both) does the actual page switch.</summary>
    public event Action<string>? NavigationRequested;

    [RelayCommand]
    private void GoToMissingConfig()
    {
        if (MissingConfigTarget is { } target) NavigationRequested?.Invoke(target);
    }

    public DashboardViewModel(
        IKeryxRuntimeBackend runtimeBackend, IGpuInfoProvider gpuInfoProvider, TierAssigner tierAssigner,
        ProfileStore profileStore, LogSink logSink, SafetyMonitor safetyMonitor)
    {
        _runtimeBackend = runtimeBackend;
        _gpuInfoProvider = gpuInfoProvider;
        _tierAssigner = tierAssigner;
        _profileStore = profileStore;
        _logSink = logSink;
        _safetyMonitor = safetyMonitor;
        _safetyMonitor.EventRaised += OnSafetyEventRaised;

        var profile = _profileStore.ActiveProfile;
        _nodeSupervisor = new ProcessSupervisor(
            _runtimeBackend, ManagedProcessKind.Node, profile.AutoRestartOnCrash,
            profile.MaxRestartAttempts, profile.RestartBaseDelaySeconds);
        _minerSupervisor = new ProcessSupervisor(
            _runtimeBackend, ManagedProcessKind.Miner, profile.AutoRestartOnCrash,
            profile.MaxRestartAttempts, profile.RestartBaseDelaySeconds);

        // Initial displayed status, set here (rather than as the [ObservableProperty] field
        // initializer above) so it goes through AppStrings and reflects whatever language is
        // active when this ViewModel is actually constructed, not a hardcoded default that would
        // only ever be correct for Russian.
        NodeStatus = AppStrings.Get("Str_Dashboard_NodeStatus_Stopped");
        MinerStatus = AppStrings.Get("Str_Dashboard_MinerStatus_Stopped");

        _nodeSupervisor.EventRaised += evt => App.Current.Dispatcher.Invoke(() =>
        {
            NodeStatus = _nodeSupervisor.IsRunning
                ? AppStrings.Get("Str_Dashboard_NodeStatus_Running")
                : AppStrings.Get("Str_Dashboard_NodeStatus_Stopped");
            LastActionMessage = AppStrings.Format("Str_Dashboard_LogPrefix_Node", evt.Message);
        });
        _minerSupervisor.EventRaised += evt => App.Current.Dispatcher.Invoke(() =>
        {
            MinerStatus = _minerSupervisor.IsRunning
                ? AppStrings.Get("Str_Dashboard_MinerStatus_Running")
                : AppStrings.Get("Str_Dashboard_MinerStatus_Stopped");
            LastActionMessage = AppStrings.Format("Str_Dashboard_LogPrefix_Miner", evt.Message);
        });
    }

    /// <summary>Non-localized run-state signal for callers (e.g. App.xaml.cs's tray-icon-state
    /// logic) that need to know whether the node/miner is actually running - added because that
    /// code used to compare NodeStatus/MinerStatus against the hardcoded Russian literal
    /// "Работает", which broke (tray always showed "Stopped" color) the moment a user picked any
    /// other language, since NodeStatus/MinerStatus are now real localized display text. Reading
    /// the same ProcessSupervisor.IsRunning this ViewModel already uses to compute those display
    /// strings can never disagree with them, and is immune to language switches by construction.</summary>
    public bool IsNodeRunning => _nodeSupervisor.IsRunning;

    /// <summary>See <see cref="IsNodeRunning"/>.</summary>
    public bool IsMinerRunning => _minerSupervisor.IsRunning;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var devices = await _gpuInfoProvider.QueryAsync();
            ActiveGpuCount = devices.Count;
        }
        catch (GpuQueryException ex)
        {
            LastActionMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StartAllAsync()
    {
        var profile = _profileStore.ActiveProfile;
        MissingConfigTarget = null;

        if (string.IsNullOrWhiteSpace(profile.NodeExecutablePath) && !IsMockBackend)
        {
            LastActionMessage = AppStrings.Get("Str_Dashboard_MissingNodePath");
            MissingConfigTarget = "Node";
            return;
        }
        if (string.IsNullOrWhiteSpace(profile.MiningAddress) && !IsMockBackend)
        {
            LastActionMessage = AppStrings.Get("Str_Dashboard_MissingMiningAddress");
            MissingConfigTarget = "Miner";
            return;
        }

        try
        {
            if (profile.AutoStartNode)
            {
                var nodeArgs = NodeArgumentBuilder.Build(profile, appDataDir: null);
                var nodeSpec = new NodeLaunchSpec(
                    ExecutablePath: profile.NodeExecutablePath,
                    Arguments: nodeArgs,
                    WorkingDirectory: DirectoryOf(profile.NodeExecutablePath),
                    EnvironmentVariables: profile.EnvironmentVariables,
                    OnOutputLine: (line, isError) => _logSink.Append(ManagedProcessKind.Node, isError, line));
                await _nodeSupervisor.StartNodeAsync(nodeSpec);
            }

            if (profile.AutoStartMiner)
            {
                // Reads the GPU page's actual per-card choices (persisted to
                // profile.GpuAssignments) via the same resolver the Miner page's preview uses, so
                // the command that's actually launched can never disagree with what the user saw
                // on either page (see GpuAssignmentResolver doc comment / PROJECT_STATUS.md).
                var devices = await TryQueryGpusAsync();
                var (gpuAssignments, anyManualOverride) = GpuAssignmentResolver.Resolve(devices, profile, _tierAssigner);
                var minerArgs = MinerArgumentBuilder.Build(profile, gpuAssignments, anyManualOverride);

                // Defense-in-depth (PROJECT_STATUS.md "In progress" item 5): --force-model already
                // communicates a disabled GPU to keryx-miner by omitting its tier token from the
                // CSV list, but that's a purely positional convention the miner's own CLI parser
                // has to honor correctly. Also setting CUDA_VISIBLE_DEVICES restricts which GPUs
                // the CUDA runtime itself exposes to the process at all - a second, independent
                // layer that holds even if a future keryx-miner version mis-parses --force-model,
                // or a user runs it with a hand-edited command line. Only set when the GPU query
                // actually succeeded (devices.Count > 0): if it failed, TryQueryGpusAsync already
                // falls back to "no assistance, let the miner auto-fit unassisted" - forcing an
                // empty/wrong CUDA_VISIBLE_DEVICES in that case would fight that fallback instead
                // of getting out of its way.
                var minerEnv = new Dictionary<string, string>(profile.EnvironmentVariables);
                if (devices.Count > 0)
                {
                    var enabledCudaIndexes = devices
                        .OrderBy(d => d.CudaIndex)
                        .Zip(gpuAssignments, (device, tier) => (device.CudaIndex, tier))
                        .Where(x => x.tier is not null)
                        .Select(x => x.CudaIndex);
                    minerEnv["CUDA_VISIBLE_DEVICES"] = MinerArgumentBuilder.BuildCudaVisibleDevices(enabledCudaIndexes);
                }

                var minerSpec = new MinerLaunchSpec(
                    ExecutablePath: profile.MinerExecutablePath,
                    Arguments: minerArgs,
                    WorkingDirectory: DirectoryOf(profile.MinerExecutablePath),
                    EnvironmentVariables: minerEnv,
                    OnOutputLine: (line, isError) => _logSink.Append(ManagedProcessKind.Miner, isError, line));
                await _minerSupervisor.StartMinerAsync(minerSpec);

                // Overheat protection (brief §14) only matters while the miner is actually
                // generating heat - tied to the miner's own start/stop rather than running
                // continuously in the background regardless of mining state.
                if (_profileStore.Settings.SafetyMonitorEnabled)
                {
                    _safetyMonitor.Start(
                        _profileStore.Settings.MonitoringIntervalSeconds,
                        _profileStore.Settings.GpuWarningTempC,
                        _profileStore.Settings.GpuCriticalTempC);
                }
            }

            LastActionMessage = AppStrings.Get("Str_Dashboard_LaunchInitiated");
        }
        catch (Exception ex)
        {
            LastActionMessage = AppStrings.Format("Str_Dashboard_LaunchFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopAllAsync()
    {
        await _nodeSupervisor.StopAsync(TimeSpan.FromSeconds(10));
        await _minerSupervisor.StopAsync(TimeSpan.FromSeconds(10));
        _safetyMonitor.Stop();
        NodeStatus = AppStrings.Get("Str_Dashboard_NodeStatus_Stopped");
        MinerStatus = AppStrings.Get("Str_Dashboard_MinerStatus_Stopped");
        LastActionMessage = CoreStrings.Get("Process.StoppedByUser");
    }

    /// <summary>
    /// SafetyMonitor.EventRaised fires from its own background polling loop (see
    /// SafetyMonitor.LoopAsync), not the UI thread - property writes are marshaled via the
    /// dispatcher (matching the existing ProcessSupervisor.EventRaised subscriptions above), while
    /// the actual stop-all await runs on the background thread since ProcessSupervisor.StopAsync
    /// needs no UI affinity.
    /// </summary>
    private void OnSafetyEventRaised(SafetyEvent evt)
    {
        App.Current.Dispatcher.Invoke(() => LastActionMessage = AppStrings.Format("Str_Dashboard_LogPrefix_Safety", evt.Message));
        if (evt.Level == SafetyLevel.Critical)
        {
            _ = StopForOverheatAsync();
        }
    }

    private async Task StopForOverheatAsync()
    {
        await _nodeSupervisor.StopAsync(TimeSpan.FromSeconds(10));
        await _minerSupervisor.StopAsync(TimeSpan.FromSeconds(10));
        _safetyMonitor.Stop();
        App.Current.Dispatcher.Invoke(() =>
        {
            NodeStatus = AppStrings.Get("Str_Dashboard_NodeStatus_Stopped");
            MinerStatus = AppStrings.Get("Str_Dashboard_MinerStatus_Stopped");
            LastActionMessage = AppStrings.Get("Str_Dashboard_StoppedForOverheat");
        });
    }

    /// <summary>
    /// A failed GPU query (e.g. nvidia-smi missing/errored) must not block launch entirely - fall
    /// back to an empty device list, which makes GpuAssignmentResolver return an empty assignment
    /// list and anyManualOverride=false, i.e. the miner's own auto-fit runs unassisted exactly as
    /// if the GPU page had never been touched.
    /// </summary>
    private async Task<IReadOnlyList<GpuDevice>> TryQueryGpusAsync()
    {
        try
        {
            return await _gpuInfoProvider.QueryAsync();
        }
        catch (GpuQueryException ex)
        {
            LastActionMessage = AppStrings.Format("Str_Dashboard_GpuQueryFailedBeforeLaunch", ex.Message);
            return Array.Empty<GpuDevice>();
        }
    }

    private bool IsMockBackend => _runtimeBackend.Name == "mock";

    private static string DirectoryOf(string executablePath) =>
        string.IsNullOrWhiteSpace(executablePath)
            ? AppDomain.CurrentDomain.BaseDirectory
            : (Path.GetDirectoryName(executablePath) ?? AppDomain.CurrentDomain.BaseDirectory);
}
