using System.Windows;
using KeryxNodeManager.Core.Localization;

namespace KeryxNodeManager.App;

/// <summary>
/// Brief §16 localization, first increment: swaps the "Strings.{lang}.xaml" ResourceDictionary
/// into Application.Current.Resources.MergedDictionaries at runtime, piggybacking on the same
/// merge mechanism App.xaml already uses for DarkTheme.xaml - the difference is this one is
/// swappable *after* startup, not just loaded once. XAML consumers must bind with
/// {DynamicResource Str_Xxx}, never {StaticResource} - StaticResource is resolved once at load
/// time and won't pick up a later swap, which would silently defeat live language switching
/// without an app restart.
///
/// "ru", "en", "es", "it", "fr", "uk", and "de" are implemented (see Strings.{code}.xaml) - an
/// unrecognized AppSettings.Language value falls back to "ru" rather than throwing, since a
/// bad/old persisted value must never crash startup.
/// </summary>
public static class LocalizationManager
{
    private static ResourceDictionary? _current;

    public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "ru", "en", "es", "it", "fr", "uk", "de" };

    /// <summary>
    /// Raised after every successful <see cref="Apply"/> call (both the startup call and every
    /// later language switch). 0.2.7 fix: added because DashboardViewModel.NodeStatus/MinerStatus
    /// (and similar ViewModel properties elsewhere that build C# display text via AppStrings.Get)
    /// resolve their text ONCE, at the moment they're computed, and store the result as a plain
    /// cached string - not a DynamicResource-style live reference. A real screenshot from the 0.2.7
    /// visual-acceptance re-review caught this live: after switching Settings' language dropdown
    /// from German back to English, every XAML-bound label updated immediately (as expected), but
    /// the Dashboard's Node/Miner status text stayed stuck showing "Gestoppt" until the next actual
    /// start/stop event recomputed it. Any ViewModel with the same cached-string pattern should
    /// subscribe here and recompute its own cached text; this event deliberately carries no
    /// arguments (subscribers already know which keys they used to compute their own properties).
    /// </summary>
    public static event Action? LanguageChanged;

    public static void Apply(string languageCode)
    {
        string normalized = SupportedLanguages.Contains(languageCode) ? languageCode : "ru";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{normalized}.xaml", UriKind.Relative),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;

        // Core has no XAML resource dictionaries of its own (Core.csproj is deliberately not
        // net8.0-windows/WPF - see CoreStrings' own doc comment), so its Russian/English/etc.
        // exception and status messages are kept in sync with the App-layer language choice here,
        // in the one place this method is already called both at startup and on every later
        // language switch (SettingsViewModel's language ComboBox binding).
        CoreStrings.Language = normalized;

        LanguageChanged?.Invoke();
    }
}
