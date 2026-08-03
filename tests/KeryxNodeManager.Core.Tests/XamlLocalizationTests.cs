using System.Text.RegularExpressions;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// 0.2.7 corrective-pass requirement (visual acceptance brief follow-up, 2026-08-03): "Add a test
/// verifying that all user-facing strings on Dashboard, GPU, and Settings come from resources, not
/// hardcoded in XAML/C#." This is a plain text/regex scan of the three Views' raw XAML, not a WPF
/// runtime test - deliberately kept in KeryxNodeManager.Core.Tests (net8.0, no WPF dependency)
/// rather than a new net8.0-windows test project, since the check itself only needs File.ReadAllText
/// and a regex, and adding a whole new WPF-only test project for one regression test would be
/// disproportionate.
///
/// What this catches: a literal (non-{Binding}/{DynamicResource}/{StaticResource}-prefixed) value on
/// a Text=/Content= attribute that either contains Cyrillic characters (the app's original
/// hardcoded-Russian-strings bug pattern) or matches one of the specific known-English strings the
/// 0.2.7 corrective brief called out by name (Overview, Start All, Stop All, Refresh, Startup, Logs,
/// Monitoring, Overheat protection, Warning threshold, Critical threshold, etc.) that a regression
/// could silently re-hardcode.
///
/// What this deliberately does NOT catch (documented limitation, not a gap to silently paper over):
/// - Internal/debug-only text, XML namespace declarations, x:Name/x:Key/x:Class attribute values,
///   route/page keys used purely as data (e.g. MainViewModel.Pages), non-Text/Content attributes.
/// - Genuinely intentional literal text that's the same across all languages by design (e.g.
///   "GPU", numeric-only values, single symbols).
/// - C# code-behind hardcoded strings (the brief explicitly says "XAML/C#" but a C#-string scanner
///   would need much more nuanced parsing to avoid false positives on log messages, exception text,
///   etc.; out of scope for this first increment - see PROJECT_STATUS.md).
/// </summary>
public class XamlLocalizationTests
{
    private static readonly string[] ViewRelativePaths =
    {
        "src/KeryxNodeManager.App/Views/DashboardView.xaml",
        "src/KeryxNodeManager.App/Views/GpuView.xaml",
        "src/KeryxNodeManager.App/Views/SettingsView.xaml",
    };

    // Known-English strings the 0.2.7 brief named explicitly as having leaked into the German UI
    // (or that are exactly the kind of static label most likely to regress back to a hardcoded
    // literal). Matched case-sensitively as whole attribute values, not substrings, to avoid false
    // positives against words that legitimately appear inside longer DynamicResource-driven runtime
    // content (which this test never sees anyway, since it only scans the XAML source).
    private static readonly string[] KnownHardcodedEnglishStrings =
    {
        "Overview", "Dashboard", "Start All", "Stop All", "Refresh",
        "Startup", "Logs", "Monitoring", "Overheat protection",
        "Warning threshold", "Critical threshold", "Settings",
    };

    // Legitimate exceptions: the language picker's own <ComboBoxItem Content="..."/> entries are
    // each language's name written in its OWN native script (this is the universal convention every
    // language switcher uses - "Русский" and "Українська" are not supposed to translate when some
    // other language is selected, same as "Deutsch"/"English"/"Español" etc. right next to them
    // already being literal Latin text that this scanner never flagged in the first place). Without
    // this allowlist, the Cyrillic check below would incorrectly flag these two as un-localized
    // Russian UI text, which they are not.
    private static readonly string[] AllowedLiteralStrings =
    {
        "Русский", "Українська",
    };

    private static readonly Regex AttributeValueRegex = new(
        @"(?:Text|Content)\s*=\s*""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex XmlCommentRegex = new(
        @"<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KeryxNodeManager.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate KeryxNodeManager.sln by walking up from " + AppContext.BaseDirectory);
        }

        return dir.FullName;
    }

    private static bool IsResourceOrBindingExpression(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('{') && trimmed.EndsWith('}');
    }

    private static bool ContainsCyrillic(string value)
    {
        foreach (var ch in value)
        {
            if (ch is >= 'Ѐ' and <= 'ӿ') return true;
        }

        return false;
    }

    public static IEnumerable<object[]> ViewFiles() => ViewRelativePaths.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(ViewFiles))]
    public void View_HasNoHardcodedUserFacingStrings(string relativePath)
    {
        var fullPath = Path.Combine(GetRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath), $"Expected view file not found: {fullPath}");

        var raw = File.ReadAllText(fullPath);
        var withoutComments = XmlCommentRegex.Replace(raw, string.Empty);

        var offenders = new List<string>();
        foreach (Match match in AttributeValueRegex.Matches(withoutComments))
        {
            var value = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (IsResourceOrBindingExpression(value)) continue;
            if (AllowedLiteralStrings.Contains(value)) continue;

            if (ContainsCyrillic(value) || KnownHardcodedEnglishStrings.Contains(value))
            {
                offenders.Add(value);
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{relativePath} contains hardcoded user-facing string(s) instead of a " +
            $"{{DynamicResource ...}}/{{Binding ...}} reference: {string.Join(", ", offenders.Select(o => $"\"{o}\""))}");
    }
}
