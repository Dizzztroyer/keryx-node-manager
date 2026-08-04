using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KeryxNodeManager.App.Converters;

/// <summary>
/// Collapses an element when the bound int count is 0 (or not an int at all), shows it otherwise.
/// Used by the Dashboard wallet card's "Недавние поступления" header, which should only appear
/// once there's actually at least one recent-entries row to head - matching this project's
/// existing StringToVisibilityConverter convention of hiding a section entirely rather than
/// showing an empty/misleading header.
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
