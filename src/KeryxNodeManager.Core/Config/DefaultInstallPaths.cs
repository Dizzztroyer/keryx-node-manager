using KeryxNodeManager.Core.Updates;

namespace KeryxNodeManager.Core.Config;

/// <summary>
/// Sane, zero-input default locations used to remove the "укажите путь к..." friction a brand new
/// user otherwise hits on first run (brief follow-up, 2026-08-03: the manual-path fields for
/// keryxd.exe/keryx-miner.exe/the models folder are exactly the kind of prompt a mainstream user
/// -- not just a developer testing this app -- should never have to answer). These are only
/// DEFAULTS: BrowseExecutable/BrowseModelsDirectory on the Node/Miner pages (and the wizard) still
/// let anyone point at a different location, e.g. an existing keryxd install or a drive with more
/// free space - nothing here forces this location, it just means a field never HAS to be filled in
/// by hand before the app becomes usable.
///
/// Everything lives under %LocalAppData%\KeryxNodeManager, matching the existing convention already
/// used for settings/profiles/logs/the discovered-nodes cache (see ConfigStore, LogFileService,
/// PublicNodeListViewModel's discoveredCachePath) - one well-known root, not a new one.
/// </summary>
public static class DefaultInstallPaths
{
    private static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeryxNodeManager");

    /// <summary>Where auto-installed keryxd.exe/keryx-miner.exe are placed if the user hasn't
    /// pointed at an existing install of their own.</summary>
    public static string BinDirectory => Path.Combine(RootDirectory, "bin");

    /// <summary>Where models are downloaded to if the user hasn't chosen a different folder - e.g.
    /// via Models page/wizard Browse.</summary>
    public static string ModelsDirectory => Path.Combine(RootDirectory, "Models");

    public static string ExecutablePathFor(ManagedBinaryKind kind) =>
        Path.Combine(BinDirectory, KeryxRepos.ExeFileNameFor(kind));
}
