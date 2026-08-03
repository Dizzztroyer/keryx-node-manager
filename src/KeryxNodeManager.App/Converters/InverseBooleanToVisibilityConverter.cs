using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KeryxNodeManager.App.Converters;

/// <summary>
/// Inverse of the built-in BooleanToVisibilityConverter: true collapses, false shows. Used for
/// the wizard's Finish-step address warning (PROJECT_STATUS.md "In progress" item 7), which
/// should only be visible when IsAddressValid is false.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
