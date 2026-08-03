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

    /// <summary>0.2.7 fix: true for the one row whose endpoint/port matches the active profile's
    /// current NodeEndpoint/NodePort (i.e. the node actually in use for mining right now) - the row
    /// list otherwise gave no visual sign of which entry (if any) was actually selected after
    /// clicking "Использовать". PublicNodeListViewModel is the single place that sets this,
    /// recomputed after RefreshAsync and after UseNode/SwitchBackToOwnNode.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public PublicNodeRowViewModel(PublicNodeInfo info)
    {
        Info = info;
    }
}
