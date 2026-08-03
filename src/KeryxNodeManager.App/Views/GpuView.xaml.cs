using System.Windows.Controls;
using KeryxNodeManager.App.ViewModels;

namespace KeryxNodeManager.App.Views;

public partial class GpuView : UserControl
{
    public GpuView()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is GpuViewModel vm)
        {
            _ = vm.RefreshCommand.ExecuteAsync(null);
        }
    }
}
