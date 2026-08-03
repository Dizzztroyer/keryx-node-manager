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
    }
}
