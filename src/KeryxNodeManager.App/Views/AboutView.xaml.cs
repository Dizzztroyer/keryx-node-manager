using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace KeryxNodeManager.App.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    // Hyperlinks in WPF don't navigate anywhere on their own - RequestNavigate must be handled
    // explicitly, and opened via the OS shell (UseShellExecute) rather than System.Diagnostics.
    // Process.Start(url) directly, which no longer works for URLs on .NET Core without it.
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
