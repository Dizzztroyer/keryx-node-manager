using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Cli;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.Logging;
using KeryxNodeManager.Core.ModelAssignment;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.ModelsManagement;
using KeryxNodeManager.Core.Networking;
using KeryxNodeManager.Core.Process;
using KeryxNodeManager.Core.Runtime;
using KeryxNodeManager.Core.Safety;
using KeryxNodeManager.Core.Localization;
using System.Net.Http;

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
    /// <summary>Hides the Dashboard wallet card in the public build (2026-08-04, user's own
    /// request) pending a full wallet feature (seed import, consolidation, real transaction
    /// history) built against Keryx Labs' own desktop wallet - see
    /// docs/WALLET_INTEGRATION_PLAN.md. Same pattern as
    /// GpuOverclockSectionViewModel.UiFeatureEnabled: the View reads this compile-time constant
    /// directly (hardcoded Visibility="Collapsed" in DashboardView.xaml, not a live binding) - the
    /// RPC client, WalletRpcService, and RefreshWalletCommand behind it are left fully intact and
    /// still covered by tests, only the View is gated.</summary>
    public const bool WalletFeatureEnabled = false;

    private readonly ProcessSupervisor _nodeSupervisor;
    private readonly ProcessSupervisor _minerSupervisor;
    private readonly WalletRpcService _walletRpcService = new();
    private readonly OfficialModelDownloadService _officialModelDownloader;

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

    /// <summary>True once keryx-miner's own stderr has reported it can't load a CUDA runtime
    /// library (real-world trigger: the user's own diagnostics export showed this looping every
    /// ~20s, and it was already present in logs from BEFORE this app's own 0.2.7 changes - this is
    /// keryx-miner.exe's own bundled auto-installer failing, not a regression in this app's code).
    /// A plain user has no reason to know to go digging in the Logs page for this - surfacing it
    /// directly on the Dashboard with a one-click link to the real fix (installing the NVIDIA CUDA
    /// 12.6 toolkit themselves - a system-level, admin-elevated installer this app deliberately
    /// does not attempt to silently drive) is the difference between "just works" and "silently
    /// mines nothing forever while looking like it's running".</summary>
    [ObservableProperty]
    private bool _showCudaRuntimeWarning;

    /// <summary>True once keryx-miner reports "Plugins: []" followed by "No workers found/
    /// specified" - real-world root cause found 2026-08-04: the exe alone was installed in
    /// %LOCALAPPDATA%\KeryxNodeManager\bin without its required companion plugin DLL(s)
    /// (keryx-miner's own architecture loads its GPU mining backend from a DLL sitting next to the
    /// executable - same pattern as its kaspa-miner ancestor's libkaspacuda.dll/libkaspaopencl.dll,
    /// confirmed live: fixing the separate CUDA-toolkit issue alone did NOT fix this - the miner got
    /// as far as verifying CUDA inference successfully, then still reported zero plugins/workers).
    /// This is a "the miner installation is incomplete" problem, not something this app's code can
    /// silently repair - the correct companion file(s) must come from wherever the user obtained
    /// this exact keryx-miner build (the public GitHub releases page only has an older, differently
    /// -architected version - see this app's own Discord card on the Models page for where the team
    /// posts current builds).</summary>
    [ObservableProperty]
    private bool _showMissingPluginWarning;

    /// <summary>True once a per-device hashrate line from keryx-miner's own log reports Ghash/s or
    /// higher for a single GPU - real-world trigger (2026-08-04): a user's NVIDIA CMP 90HX logged
    /// "7.13 Thash/s" while the Keryx network's own published total hashrate was ~25 GH/s at the
    /// time - a single card cannot legitimately out-hash the entire network by ~280x. This is the
    /// miner's own "hashes_tried" counter (src/miner.rs) being incremented far faster than any real
    /// PoM walk work for that specific device - almost certainly an upstream keryx-miner counting
    /// bug (possibly specific to headless CMP cards), not something this app computes or can fix
    /// in its own code (the number comes verbatim from keryx-miner.exe's stdout). Rather than
    /// silently displaying an obviously-impossible number as if it were real mining performance,
    /// this flags it - the user's actual yield is still honestly represented by accepted_blocks/
    /// escrow stats, never by this counter.</summary>
    [ObservableProperty]
    private bool _showHashrateAnomalyWarning;

    /// <summary>True while StartAllAsync is downloading a missing tier's model on the user's
    /// behalf before launching keryx-miner (2026-08-04 user follow-up: they saw a real % progress
    /// bar on the Models page's own manual download, but nothing at all during the miner's own
    /// first-run IPFS auto-download - root cause: that download happens entirely inside
    /// keryx-miner.exe itself, with no progress/byte-count of any kind in its stdout for this app
    /// to parse, see docs/KERYX_RESEARCH.md §7). Rather than inventing a fake percentage for an
    /// opaque subprocess download, EnsureModelsDownloadedAsync now proactively runs the SAME
    /// OfficialModelDownloadService the Models page's "Скачать официальную модель" button already
    /// uses - real bytes-received/total from an HTTP or torrent transfer this app itself drives -
    /// before the miner is even started, so the miner finds the model already installed and never
    /// needs its own silent fetch at all.</summary>
    [ObservableProperty]
    private bool _isDownloadingModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDownloadProgressPercentText))]
    private double _modelDownloadProgressPercent;

    /// <summary>See ModelCardViewModel.ProgressPercentText's own doc comment - same "NN%" label
    /// convention reused here rather than a second, differently-formatted copy.</summary>
    public string ModelDownloadProgressPercentText => $"{ModelDownloadProgressPercent:F0}%";

    [ObservableProperty]
    private bool _modelDownloadProgressIsIndeterminate;

    [ObservableProperty]
    private string _modelDownloadStatusText = "";

    /// <summary>Formatted "12.34567890 KRX" (or a placeholder before the first successful load) -
    /// read from keryxd's own getBalanceByAddress RPC against the profile's public MiningAddress.
    /// Never derived from anything secret: this app never reads, stores, or has any concept of a
    /// private key/seed - see WalletRpcService's doc comment.</summary>
    [ObservableProperty]
    private string _walletBalanceText = "—";

    [ObservableProperty]
    private bool _isWalletBusy;

    /// <summary>Set on any wallet refresh failure (node not running, --utxoindex not enabled yet
    /// while keryxd is still rebuilding it, no address configured, etc.) - always keryxd's own
    /// error text where one exists, per this app's "let the node be authoritative" convention
    /// (see WalletRpcService doc comment), never a guessed-at friendlier rewrite.</summary>
    [ObservableProperty]
    private string? _walletStatusMessage;

    /// <summary>Unspent outputs currently held by the mining address, newest-first - see
    /// WalletRpcService's doc comment for why this is "recent activity still sitting in the
    /// wallet," not a full transaction history (keryxd's RPC surface has no such call).</summary>
    public ObservableCollection<WalletUtxoRowViewModel> WalletRecentEntries { get; } = new();

    [RelayCommand]
    private async Task RefreshWalletAsync()
    {
        var profile = _profileStore.ActiveProfile;
        if (string.IsNullOrWhiteSpace(profile.MiningAddress))
        {
            WalletBalanceText = "—";
            WalletRecentEntries.Clear();
            WalletStatusMessage = AppStrings.Get("Str_Dashboard_Wallet_NoAddress");
            return;
        }

        IsWalletBusy = true;
        WalletStatusMessage = null;
        try
        {
            // The wRPC JSON listener is always loopback-only (NodeArgumentBuilder never binds it
            // anywhere else), and only ever answers for the keryxd instance THIS app itself
            // launches - so this always polls 127.0.0.1, never the active profile's NodeEndpoint
            // (which may point at a substitute public node with no RPC access at all). Same
            // invariant as PublicNodeListViewModel.OwnNodeRpcAddress().
            var port = profile.NodeRpcJsonPort
                ?? (profile.UseTestnet ? NodeArgumentBuilder.DefaultRpcJsonPortTestnet : NodeArgumentBuilder.DefaultRpcJsonPortMainnet);

            var snapshot = await _walletRpcService.GetSnapshotAsync(
                "127.0.0.1", port, profile.MiningAddress, maxEntries: 15, CancellationToken.None);

            WalletBalanceText = FormatSompi(snapshot.BalanceSompi);
            WalletRecentEntries.Clear();
            foreach (var entry in snapshot.RecentEntries)
            {
                WalletRecentEntries.Add(new WalletUtxoRowViewModel(
                    FormatSompi(entry.AmountSompi),
                    entry.IsCoinbase
                        ? AppStrings.Get("Str_Dashboard_Wallet_EntryTypeReward")
                        : AppStrings.Get("Str_Dashboard_Wallet_EntryTypeIncoming"),
                    ShortenTxId(entry.TransactionId)));
            }
            if (WalletRecentEntries.Count == 0)
            {
                WalletStatusMessage = AppStrings.Get("Str_Dashboard_Wallet_NoRecentEntries");
            }
        }
        catch (Exception ex)
        {
            WalletStatusMessage = AppStrings.Format("Str_Dashboard_Wallet_Error", ex.Message);
        }
        finally
        {
            IsWalletBusy = false;
        }
    }

    /// <summary>Sompi -> KRX display text, 8 decimal places (1 KRX = 100,000,000 sompi - see
    /// WalletRpcService's doc comment). Always culture-invariant so a decimal comma locale doesn't
    /// silently produce an unparseable/misleading number for a currency amount.</summary>
    private static string FormatSompi(ulong sompi) =>
        (sompi / 100_000_000m).ToString("0.00000000", CultureInfo.InvariantCulture) + " KRX";

    private static string ShortenTxId(string txId) =>
        string.IsNullOrEmpty(txId) || txId.Length <= 16 ? txId : $"{txId[..8]}…{txId[^8..]}";

    [RelayCommand]
    private void OpenCudaDownloadPage()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://developer.nvidia.com/cuda-12-6-0-download-archive")
        { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenMinerDiscord()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://discord.gg/U9eDmBUKTF")
        { UseShellExecute = true });
    }

    /// <summary>Matches keryx-miner's own real stderr text (both the WARN "installing them
    /// automatically" line and the terminal ERROR line use "CUDA runtime lib" - matching on that
    /// substring catches either, without depending on exact wording that could shift between miner
    /// versions). Only ever flips the warning ON here; it's cleared at the top of every fresh
    /// StartAllAsync so a user who fixes the toolkit and restarts mining doesn't keep seeing a
    /// stale banner from the previous run.</summary>
    private void DetectMinerCudaRuntimeIssue(string line)
    {
        if (line.Contains("CUDA runtime lib", StringComparison.OrdinalIgnoreCase))
        {
            App.Current.Dispatcher.Invoke(() => ShowCudaRuntimeWarning = true);
        }
        if (line.Contains("No workers", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Plugins: []", StringComparison.OrdinalIgnoreCase))
        {
            App.Current.Dispatcher.Invoke(() => ShowMissingPluginWarning = true);
        }
        DetectImplausibleDeviceHashrate(line);
    }

    /// <summary>Matches keryx-miner's own real per-device log line, e.g.
    /// "Device #0 (NVIDIA CMP 90HX): 7.13 Thash/s" (see MinerManager::log_hashrate in
    /// src/miner.rs - the exact "Device {id}: {rate:.2} {suffix}" format). Ghash/s or Thash/s for a
    /// single consumer/mining GPU on this algorithm is not physically plausible (see
    /// ShowHashrateAnomalyWarning's doc comment) - only ever flips the warning ON here, cleared at
    /// the top of every fresh StartAllAsync like the other miner-log-driven warnings.</summary>
    private static readonly System.Text.RegularExpressions.Regex DeviceHashrateLineRegex = new(
        @"Device\s+#\d+[^:]*:\s*[\d.]+\s*(Ghash|Thash)/s",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private void DetectImplausibleDeviceHashrate(string line)
    {
        if (DeviceHashrateLineRegex.IsMatch(line))
        {
            App.Current.Dispatcher.Invoke(() => ShowHashrateAnomalyWarning = true);
        }
    }

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
        ProfileStore profileStore, LogSink logSink, SafetyMonitor safetyMonitor, HttpClient httpClient)
    {
        _runtimeBackend = runtimeBackend;
        _gpuInfoProvider = gpuInfoProvider;
        _tierAssigner = tierAssigner;
        _profileStore = profileStore;
        _logSink = logSink;
        _safetyMonitor = safetyMonitor;
        _safetyMonitor.EventRaised += OnSafetyEventRaised;
        _officialModelDownloader = new OfficialModelDownloadService(httpClient);

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

        // 0.2.7 fix: NodeStatus/MinerStatus are plain cached strings (see the class-level property
        // declarations above), resolved via AppStrings.Get at the moment each ProcessSupervisor
        // event fires - they are NOT DynamicResource references, so nothing re-evaluates them when
        // the user later switches languages on the Settings page while the node/miner are simply
        // sitting idle (no new supervisor event to trigger a recompute). Verified live: this is
        // exactly why a real screenshot showed "Gestoppt" surviving a switch back to English on the
        // Dashboard, while every XAML-bound label on the same page updated immediately.
        // LocalizationManager.LanguageChanged (raised by every Apply() call, including this one)
        // lets this ViewModel recompute its own cached text the same way a supervisor event does,
        // without needing the node/miner to actually start or stop first. DashboardViewModel is
        // constructed once via DI and lives for the app's process lifetime (MainWindow.xaml.cs
        // reuses the same instance across every Dashboard navigation, only recreating the View), so
        // this subscription is never unsubscribed - there is exactly one instance to leak.
        LocalizationManager.LanguageChanged += RefreshLocalizedStatusText;
    }

    private void RefreshLocalizedStatusText()
    {
        NodeStatus = _nodeSupervisor.IsRunning
            ? AppStrings.Get("Str_Dashboard_NodeStatus_Running")
            : AppStrings.Get("Str_Dashboard_NodeStatus_Stopped");
        MinerStatus = _minerSupervisor.IsRunning
            ? AppStrings.Get("Str_Dashboard_MinerStatus_Running")
            : AppStrings.Get("Str_Dashboard_MinerStatus_Stopped");
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
        ShowCudaRuntimeWarning = false;
        ShowMissingPluginWarning = false;
        ShowHashrateAnomalyWarning = false;
        IsDownloadingModel = false;
        ModelDownloadStatusText = "";

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

                // Real-progress model pre-download (2026-08-04 user follow-up - see
                // IsDownloadingModel's own doc comment for the full "why"): make sure every tier
                // this launch actually assigned to a GPU is already installed BEFORE the miner
                // starts, so keryx-miner never has to fall back to its own silent, unparseable
                // IPFS fetch in the first place.
                await EnsureModelsDownloadedAsync(gpuAssignments);

                var minerSpec = new MinerLaunchSpec(
                    ExecutablePath: profile.MinerExecutablePath,
                    Arguments: minerArgs,
                    WorkingDirectory: DirectoryOf(profile.MinerExecutablePath),
                    EnvironmentVariables: minerEnv,
                    OnOutputLine: (line, isError) =>
                    {
                        _logSink.Append(ManagedProcessKind.Miner, isError, line);
                        DetectMinerCudaRuntimeIssue(line);
                    });
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

    /// <summary>Downloads every tier actually assigned to a GPU this launch (deduplicated - two
    /// cards on the same tier only download it once) that isn't already installed, using the exact
    /// same OfficialModelDownloadService the Models page's own "Скачать официальную модель" button
    /// drives - real HTTP/torrent byte-progress, not a guess. Runs sequentially, before the miner
    /// process is started, specifically so the miner finds the file already there and never falls
    /// back to its own silent IPFS fetch (see IsDownloadingModel's doc comment for the full
    /// motivation). A missing official mirror or a failed download does not block the miner launch
    /// - it just means that tier's model may still not exist when the miner starts, in which case
    /// the miner's own (progress-less) auto-download is the same fallback that existed before this
    /// method was added, not a new failure mode.</summary>
    private async Task EnsureModelsDownloadedAsync(IReadOnlyList<ModelTier?> gpuAssignments)
    {
        var profile = _profileStore.ActiveProfile;
        var modelsDir = profile.ModelsDirectory;
        if (string.IsNullOrWhiteSpace(modelsDir))
        {
            modelsDir = DefaultInstallPaths.ModelsDirectory;
            profile.ModelsDirectory = modelsDir;
            await _profileStore.SaveAsync();
        }

        var neededTiers = gpuAssignments.Where(t => t is not null).Select(t => t!.Value).Distinct();
        foreach (var tier in neededTiers)
        {
            var spec = ModelTierCatalog.Get(tier);
            if (ModelFileLocator.IsInstalled(modelsDir, spec.DirName)) continue;

            var mirror = OfficialModelMirrors.TryGet(tier);
            if (mirror is null)
            {
                // No verified mirror for this tier (defensive only - every tier currently has one,
                // see OfficialModelMirrors) - fall back to the miner's own silent IPFS download
                // exactly as before this feature existed. Nothing this app can do here produces a
                // real percentage for that path (see IsDownloadingModel's doc comment), so an
                // honest "please wait, no progress available" note is better than either a fake
                // bar or total silence.
                ModelDownloadStatusText = AppStrings.Format("Str_Dashboard_ModelAutoDownloadNoMirror", spec.Name);
                continue;
            }

            IsDownloadingModel = true;
            ModelDownloadProgressIsIndeterminate = true;
            ModelDownloadProgressPercent = 0;
            ModelDownloadStatusText = AppStrings.Format("Str_Dashboard_ModelAutoDownloadStarting", spec.Name);

            var progress = new Progress<OfficialModelDownloadProgress>(p =>
            {
                if (p.TotalBytes is long total && total > 0)
                {
                    ModelDownloadProgressIsIndeterminate = false;
                    ModelDownloadProgressPercent = 100.0 * p.BytesReceived / total;
                    ModelDownloadStatusText = AppStrings.Format(
                        "Str_Dashboard_ModelAutoDownloadProgress", spec.Name, p.Phase, ModelDownloadProgressPercent.ToString("F1"));
                }
                else
                {
                    ModelDownloadProgressIsIndeterminate = true;
                    ModelDownloadStatusText = AppStrings.Format("Str_Dashboard_ModelAutoDownloadProgressNoTotal", spec.Name, p.Phase);
                }
            });

            try
            {
                var source = mirror.TorrentUrl ?? mirror.DirectUrl;
                await _officialModelDownloader.DownloadAndInstallAsync(spec, source, modelsDir, progress, CancellationToken.None);
                ModelDownloadStatusText = AppStrings.Format("Str_Dashboard_ModelAutoDownloadComplete", spec.Name);
            }
            catch (Exception ex)
            {
                // Same "don't block the launch on it" fallback as the no-mirror branch above - the
                // miner can still attempt its own IPFS fetch for this tier.
                ModelDownloadStatusText = AppStrings.Format("Str_Dashboard_ModelAutoDownloadError", spec.Name, ex.Message);
            }
            finally
            {
                IsDownloadingModel = false;
            }
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

/// <summary>Plain display row for the Dashboard wallet card's recent-entries list - already
/// formatted (amount text, localized type label, shortened tx id) so the View binds directly with
/// no converters. Immutable/no INotifyPropertyChanged: rebuilt fresh on every RefreshWalletAsync,
/// never mutated in place.</summary>
public sealed class WalletUtxoRowViewModel
{
    public string AmountText { get; }
    public string TypeText { get; }
    public string TransactionIdText { get; }

    public WalletUtxoRowViewModel(string amountText, string typeText, string transactionIdText)
    {
        AmountText = amountText;
        TypeText = typeText;
        TransactionIdText = transactionIdText;
    }
}
