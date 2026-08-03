using System.Windows;

namespace KeryxNodeManager.App;

/// <summary>
/// Brief follow-up (2026-08-03) light/dark theme toggle: mirrors LocalizationManager's exact
/// mechanism - swaps a "{Dark,Light}Theme.xaml" ResourceDictionary into
/// Application.Current.Resources.MergedDictionaries at runtime, tracking the previously-added
/// dictionary in a static field so it can be removed before the new one is added, rather than
/// merging theme dictionaries on top of each other.
///
/// This is what makes {DynamicResource} mandatory (not {StaticResource}) for every theme brush
/// (BackgroundBrush/SurfaceBrush/.../ErrorBrush) and every named theme Style
/// (CardStyle/PrimaryButtonStyle/SecondaryButtonStyle/NavButtonStyle/ComboBoxToggleButtonStyle)
/// plus the untyped Window/ComboBoxItem/ComboBox styles, everywhere they're referenced - both
/// inside DarkTheme.xaml/LightTheme.xaml's own Setters/Triggers/ControlTemplates and in every
/// View's XAML. StaticResource resolves once when the XAML is loaded and bakes the resolved
/// brush/style object directly onto the property; it never re-resolves when Apply() below later
/// removes that dictionary and adds a different one, so a StaticResource reference would leave
/// already-rendered controls frozen on whichever theme was active at first paint. This exact
/// failure mode is why LocalizationManager's language strings already use DynamicResource - the
/// same reasoning applies here, just for colors/styles instead of text.
///
/// "dark" and "light" are implemented (see {Dark,Light}Theme.xaml) - an unrecognized
/// AppSettings.Theme value falls back to "dark" rather than throwing, since a bad/old persisted
/// value must never crash startup.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _current;

    public static readonly IReadOnlyList<string> SupportedThemes = new[] { "dark", "light" };

    public static void Apply(string themeCode)
    {
        string normalized = SupportedThemes.Contains(themeCode) ? themeCode : "dark";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Resources/{(normalized == "light" ? "LightTheme" : "DarkTheme")}.xaml", UriKind.Relative),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
    }
}
