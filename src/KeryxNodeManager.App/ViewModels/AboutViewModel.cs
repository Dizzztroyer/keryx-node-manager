using CommunityToolkit.Mvvm.ComponentModel;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Real About page: version now comes from AppVersionInfo (the actual assembly version) rather
/// than a hand-maintained literal - see AppVersionInfo's doc comment for why that mattered.
/// Repo/website links point at the Keryx *protocol's* own upstream repos (keryx-node/keryx-miner)
/// and keryx-labs.com, per docs/KERYX_RESEARCH.md - this app itself has no public repo of its own
/// yet (still a local working copy, per PROJECT_STATUS.md's "Last verified commit" section), so
/// that is stated plainly rather than inventing a URL that doesn't exist.
/// </summary>
public partial class AboutViewModel : ObservableObject
{
    public string AppVersion => AppVersionInfo.Current;
    public string OperatingSystem => Environment.OSVersion.ToString();
    public string DotNetRuntime => Environment.Version.ToString();
    public bool Is64Bit => Environment.Is64BitOperatingSystem;
}
