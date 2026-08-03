using System.Windows.Controls;
using KeryxNodeManager.App.ViewModels;

namespace KeryxNodeManager.App.Views;

public partial class ModelsView : UserControl
{
    public ModelsView()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Re-checks each card's install/paused state against the real filesystem every time the
        // page is opened - the user may have switched ModelsDirectory on the Miner page, or
        // dropped/removed a .gguf file manually, since the last visit.
        if (DataContext is ModelsViewModel vm)
        {
            vm.RefreshCommand.Execute(null);
        }
    }
}
