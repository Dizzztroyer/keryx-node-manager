using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KeryxNodeManager.App.Converters;

/// <summary>
/// Collapses an element when the bound string is null/empty/whitespace, shows it otherwise.
/// Used for the Models page's aggregate disk-usage summary (PROJECT_STATUS.md "In progress"
/// item 6), which is deliberately an empty string - not a placeholder sentence - when there is
/// no models directory set or nothing installed yet, so the line should take up no visible space
/// rather than showing a misleading "0 models installed" line to a first-run user.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
