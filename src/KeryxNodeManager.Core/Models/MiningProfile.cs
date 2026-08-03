namespace KeryxNodeManager.Core.Models;

public sealed class MiningProfile
{
    public string Name { get; set; } = "Default";
    public string MiningAddress { get; set; } = string.Empty;
    public string NodeEndpoint { get; set; } = "127.0.0.1";
    public int? NodePort { get; set; }
    public bool UseTestnet { get; set; }
    /// <summary>Full path to keryxd.exe. Empty until the user points at a downloaded binary -
    /// the app never bundles or auto-downloads it (brief §20: verify binary provenance).</summary>
    public string NodeExecutablePath { get; set; } = string.Empty;
    /// <summary>Full path to keryx-miner.exe. Same rule as NodeExecutablePath.</summary>
    public string MinerExecutablePath { get; set; } = string.Empty;

    /// <summary>The GitHub release tag (e.g. "v1.4.4-OPoI") this app itself downloaded and wrote
    /// to NodeExecutablePath, if any - null/empty means either nothing has been downloaded through
    /// the app's own updater yet (the user pointed at a manually-installed binary) or the path was
    /// changed since. Never inferred by asking the exe its own version (keryxd.exe/keryx-miner.exe
    /// have no confirmed --version flag - see docs/KERYX_RESEARCH.md), only ever set by
    /// BinaryUpdateService after a successful ApplyUpdate.</summary>
    public string? NodeInstalledVersion { get; set; }
    /// <summary>Same as NodeInstalledVersion, for MinerExecutablePath/keryx-miner.exe.</summary>
    public string? MinerInstalledVersion { get; set; }
    public string ModelsDirectory { get; set; } = string.Empty;

    /// <summary>Directory keryxd.exe should store its blockchain data in, passed as --appdir on
    /// launch (see NodeArgumentBuilder). Empty means "let keryxd use its own OS-default location" -
    /// this field was plumbed through NodeArgumentBuilder from early on but was never actually
    /// populated (DashboardViewModel always passed null), so --appdir was silently never emitted;
    /// this is the fix, wired up alongside the DataDirDownloadService one-click download feature.</summary>
    public string NodeDataDirectory { get; set; } = string.Empty;

    /// <summary>Whether to launch keryxd with --rpclisten-json so this app's own
    /// KeryxRpcJsonClient can query it (real peer list, sync status) - see docs/KERYX_RESEARCH.md
    /// for the confirmed RPC ops (GetServerInfo/GetBlockDagInfo/GetConnectedPeerInfo/
    /// GetPeerAddresses). Off by default in keryxd itself, but on by default here since the app's
    /// own Node page features depend on it; always bound to 127.0.0.1 (see NodeArgumentBuilder),
    /// never 0.0.0.0, so this never exposes RPC beyond the local machine.</summary>
    public bool NodeRpcJsonEnabled { get; set; } = true;

    /// <summary>Port for the above. Null means "use keryxd's own default" (24110 mainnet / 24210
    /// testnet per keryxd --help) - NodeArgumentBuilder only emits an explicit port if this is set.</summary>
    public int? NodeRpcJsonPort { get; set; }

    public string IpfsUrl { get; set; } = "http://127.0.0.1:5001";
    public decimal DevFundPercent { get; set; } = 2.0m;
    public List<GpuAssignment> GpuAssignments { get; set; } = new();
    public string RuntimeBackend { get; set; } = "native"; // "native" | "wsl" | "mock"
    public bool AutoStartNode { get; set; } = true;
    public bool AutoStartMiner { get; set; } = true;
    public bool WaitForNodeReady { get; set; } = true;
    public bool AutoRestartOnCrash { get; set; } = true;
    public int MaxRestartAttempts { get; set; } = 5;
    public int RestartBaseDelaySeconds { get; set; } = 5;
    public List<string> ExtraMinerArguments { get; set; } = new();
    public List<string> ExtraNodeArguments { get; set; } = new();
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>User-supplied download source per tier (keyed by ModelTier name, e.g.
    /// "VeryLight"), for the Models page's app-managed download (brief §7). Deliberately not
    /// pre-populated with any URL - see ModelDownloader's doc comment for why no mirror is
    /// hardcoded.</summary>
    public Dictionary<string, ModelSourceConfig> ModelSources { get; set; } = new();

    /// <summary>Per-GPU overclock/fan settings, keyed by GpuDevice.Uuid (same keying convention as
    /// GpuAssignments) - only ever set by the user explicitly applying a change on the GPU page,
    /// through the confirmation-dialog-gated flow (real hardware risk - see
    /// IGpuOverclockController's doc comment). Absence of an entry means "never touched by this
    /// app, card is at stock/driver-default settings."</summary>
    public Dictionary<string, GpuOverclockSettings> GpuOverclockSettings { get; set; } = new();
}

/// <summary>Persisted form of what the user asked for - NOT necessarily what's currently applied
/// to the card (the app re-applies this on Start All if AutoApplyOnStart is true, matching the
/// idea that a driver reset/reboot doesn't silently lose the user's chosen settings).</summary>
public sealed class GpuOverclockSettings
{
    public int CoreClockOffsetMhz { get; set; }
    public int MemoryClockOffsetMhz { get; set; }
    /// <summary>Null = automatic/driver-controlled fan curve (the safe default).</summary>
    public int? FanSpeedPercent { get; set; }
}

/// <summary>One tier's manually-entered download source. ExpectedSha256 is optional - if the user
/// doesn't have a known-good hash to check against, the download still proceeds, just without
/// tamper/corruption verification.</summary>
public sealed class ModelSourceConfig
{
    public string Url { get; set; } = string.Empty;
    public string? ExpectedSha256 { get; set; }
}

/// <summary>Root settings document persisted to settings.json. Versioned for migrations.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string ActiveProfileName { get; set; } = "Default";
    public List<MiningProfile> Profiles { get; set; } = new();
    public string Language { get; set; } = "ru";
    public string Theme { get; set; } = "dark";
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; }
    public bool CloseButtonMinimizesToTray { get; set; } = true;
    public int MonitoringIntervalSeconds { get; set; } = 5;
    public int LogRetentionDays { get; set; } = 14;
    public long MaxLogSizeMb { get; set; } = 200;
    public bool NotificationsEnabled { get; set; } = true;
    public bool AdvancedModeEnabled { get; set; }
    public bool TelemetryOptIn { get; set; } = false; // default OFF per brief §20

    /// <summary>Brief §14 overheat protection (SafetyMonitor). Defaults are conservative
    /// consumer-GPU numbers (most cards throttle themselves well above 85°C, and 95°C is close to
    /// where sustained operation risks long-term wear) - not derived from any specific card's
    /// datasheet, since the app supports arbitrary NVIDIA hardware and has no way to know a given
    /// card's actual safe ceiling. Adjustable on the Settings page.</summary>
    public bool SafetyMonitorEnabled { get; set; } = true;
    public int GpuWarningTempC { get; set; } = 85;
    public int GpuCriticalTempC { get; set; } = 95;

    /// <summary>Set once the first-run wizard (brief §4) is finished or explicitly skipped.
    /// Gates whether App.xaml.cs shows the wizard before MainWindow (see WizardWindow/App.xaml.cs).
    /// Defaults false so a fresh settings.json (new install) always sees the wizard once.</summary>
    public bool FirstRunCompleted { get; set; }
}
