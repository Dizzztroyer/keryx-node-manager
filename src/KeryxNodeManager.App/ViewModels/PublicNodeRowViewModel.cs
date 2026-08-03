using CommunityToolkit.Mvvm.ComponentModel;
using KeryxNodeManager.Core.Networking;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>One row in the Node page's public-node list - wraps the immutable Core
/// <see cref="PublicNodeInfo"/> record with the mutable per-row UI state (last ping result,
/// in-flight indicator) that record deliberately doesn't carry itself.</summary>
public partial class PublicNodeRowViewModel : ObservableObject
{
    public PublicNodeInfo Info { get; }

    [ObservableProperty]
    private string _statusText = "—";

    [ObservableProperty]
    private bool _isChecking;

    public PublicNodeRowViewModel(PublicNodeInfo info)
    {
        Info = info;
    }
}
