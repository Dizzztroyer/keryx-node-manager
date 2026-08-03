using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace KeryxNodeManager.App.Converters;

/// <summary>
/// Resolves what a themed ComboBox's closed (non-dropped-down) selection box should display,
/// covering both usage patterns in this app (PROJECT_STATUS.md "In progress" item 3, closed by
/// the themed ComboBox control template in DarkTheme.xaml):
///
/// 1. ComboBoxes whose items are literal &lt;ComboBoxItem Content="..."/&gt; elements (e.g.
///    SettingsView's language picker) - WPF's SelectedItem for these is the ComboBoxItem
///    container itself, and its real displayed text is ComboBoxItem.Content.
/// 2. Data-bound ComboBoxes using ItemsSource + DisplayMemberPath (e.g. GpuView's per-card mode
///    picker, bound to a list of GpuModeOption records) - SelectedItem here is the raw bound
///    object. The framework's own SelectionBoxItem/SelectionBoxItemTemplate machinery is
///    documented to project DisplayMemberPath onto the closed box automatically, but empirically
///    did not do so through this custom template (verified live: it rendered the record's
///    ToString(), e.g. "GpuModeOption { Value = auto, Label = ... }", instead of the Label text) -
///    so this converter reads DisplayMemberPath via reflection instead of relying on that
///    framework projection.
/// </summary>
public sealed class ComboBoxSelectionDisplayConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not object item || item is null) return null;

        if (item is ComboBoxItem cbi) return cbi.Content;

        var displayMemberPath = values[1] as string;
        if (string.IsNullOrEmpty(displayMemberPath)) return item;

        var prop = item.GetType().GetProperty(displayMemberPath);
        return prop?.GetValue(item) ?? item;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
