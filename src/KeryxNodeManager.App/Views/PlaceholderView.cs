using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeryxNodeManager.App.Views;

/// <summary>
/// Shown for every nav page not yet built out (Models/Node/Miner/Logs/Diagnostics/Settings/About
/// in this pass). Says so plainly instead of showing an empty page or a fake-populated one -
/// per the brief's own instruction not to claim a feature is done when it's only a stub.
/// </summary>
public sealed class PlaceholderView : UserControl
{
    public PlaceholderView(string pageName)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = AppStrings.Format("Str_Placeholder_NotImplemented", pageName),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = AppStrings.Get("Str_Placeholder_SeeStatus"),
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Content = stack;
    }
}
