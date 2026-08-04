using System.Windows.Controls;
using KeryxNodeManager.App.ViewModels;

namespace KeryxNodeManager.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            _ = vm.RefreshCommand.ExecuteAsync(null);
            _ = vm.RefreshWalletCommand.ExecuteAsync(null);
        }
    }
}
