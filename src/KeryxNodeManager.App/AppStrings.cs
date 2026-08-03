using System.Globalization;
using System.Windows;

namespace KeryxNodeManager.App;

/// <summary>
/// Resource-lookup helper for App-layer runtime status/error text that is built in C#, not XAML -
/// ViewModels, tray menus, and file-dialog titles construct their strings imperatively (e.g. inside
/// an <c>if</c>/<c>catch</c> block or a lambda), so a <c>{DynamicResource}</c> binding in a .xaml
/// file can never reach them; something has to fetch the localized text at the exact moment the
/// message is produced, in code.
///
/// Deliberately reads from the same merged <c>Strings.{lang}.xaml</c> resource dictionary that
/// every existing <c>{DynamicResource Str_Xxx}</c> XAML binding already resolves against
/// (<see cref="Application.Resources"/>, populated by <c>LocalizationManager.Apply</c> at startup
/// and on every language switch) rather than introducing a second, separately-maintained string
/// table - this way a language switch updates both XAML bindings and these C#-built strings from
/// the exact same source of truth, with no separate cache to invalidate and no risk of the two
/// mechanisms drifting out of sync.
/// </summary>
public static class AppStrings
{
    /// <summary>Looks up <paramref name="key"/> in the currently-merged Strings.{lang}.xaml
    /// dictionary. Falls back to the bare key itself (never throws) if the resource is missing -
    /// same fail-soft contract as CoreStrings.Get, since this is often called while already
    /// handling an error and must not itself become a new one.</summary>
    public static string Get(string key)
        => Application.Current?.TryFindResource(key) as string ?? key;

    /// <summary>Convenience wrapper for the common case of a lookup immediately followed by
    /// <see cref="string.Format(IFormatProvider?, string, object?[])"/>, mirroring
    /// <c>CoreStrings.Format</c> in the Core layer.</summary>
    public static string Format(string key, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, Get(key), args);
}
