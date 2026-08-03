using System.Reflection;

namespace KeryxNodeManager.App;

/// <summary>
/// Single source of truth for the version string shown in the UI (nav-strip footer, diagnostic
/// ZIP export, About page). Reads the real assembly version instead of a hand-maintained literal:
/// before this, MainViewModel and LogsViewModel each hardcoded their own "0.1.0" constant with a
/// comment admitting they had to be kept in sync manually - a bump to
/// KeryxNodeManager.App.csproj's &lt;Version&gt; would have silently stopped matching what the UI
/// actually showed. `Version.ToString(3)` trims the trailing revision component
/// (AssemblyVersion's default 4th field, usually 0) so this reads "0.1.0", not "0.1.0.0".
/// </summary>
public static class AppVersionInfo
{
    public static readonly string Current =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
