using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Cli;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.Networking;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Drives the Node page's public/community node list (brief-adjacent request: "let the miner use
/// someone else's node if mine isn't ready yet, and switch back automatically once it's synced").
/// Loads the bundled default (ships empty - see PublicNodeDirectoryService's doc comment) plus,
/// if the user has set one, a remote JSON list, plus (new) real peers discovered via the user's own
/// node's RPC interface (see OwnNodePeerDiscoveryService) - each node can be pinged (a real, timed
/// TCP-connect probe) and "used", which points the active profile's NodeEndpoint/NodePort at it and
/// turns off AutoStartNode so this app doesn't also try to launch a redundant local node.
///
/// New in this increment: switching to a substitute node remembers the profile's own node settings
/// and starts a background poll (via keryxd's real getServerInfo/isSynced RPC call) of the user's
/// own node; once it reports synced, this automatically switches back and stops polling. This can
/// only work if the user's own node is actually running (with --rpclisten-json, which
/// NodeArgumentBuilder now always adds) even while a substitute node is being used for mining -
/// the app does not, and should not, silently start the local node just to poll it.
/// </summary>
public partial class PublicNodeListViewModel : ObservableObject
{
    private static readonly TimeSpan SyncPollInterval = TimeSpan.FromSeconds(20);

    private readonly PublicNodeDirectoryService _directoryService;
    private readonly OwnNodePeerDiscoveryService _discoveryService;
    private readonly MiningProfile _profile;
    private readonly Action _persist;

    private CancellationTokenSource? _syncWatchCts;
    private string? _rememberedOwnEndpoint;
    private int? _rememberedOwnPort;
    private bool _rememberedAutoStartNode;

    public ObservableCollection<PublicNodeRowViewModel> Nodes { get; } = new();

    [ObservableProperty]
    private string? _remoteListUrl;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>True while this ViewModel is actively polling the user's own node for sync
    /// completion (started by UseNode when switching away from the local node). Bound by the View
    /// to show a "watching for sync..." indicator.</summary>
    [ObservableProperty]
    private bool _isWatchingForOwnNodeSync;

    public PublicNodeListViewModel(
        PublicNodeDirectoryService directoryService,
        OwnNodePeerDiscoveryService discoveryService,
        MiningProfile profile,
        Action persist)
    {
        _directoryService = directoryService;
        _discoveryService = discoveryService;
        _profile = profile;
        _persist = persist;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        Nodes.Clear();
        try
        {
            var bundled = _directoryService.LoadBundled();
            IReadOnlyList<PublicNodeInfo> remote = Array.Empty<PublicNodeInfo>();
            if (!string.IsNullOrWhiteSpace(RemoteListUrl) && Uri.TryCreate(RemoteListUrl, UriKind.Absolute, out var uri))
            {
                remote = await _directoryService.FetchRemoteAsync(uri, CancellationToken.None);
            }

            foreach (var node in bundled.Concat(remote))
            {
                Nodes.Add(new PublicNodeRowViewModel(node));
            }

            StatusMessage = Nodes.Count == 0
                ? "Список пуст. Если у вас есть ссылка на публичный список нод, укажите её выше, или нажмите «Найти через свою ноду»."
                : $"Загружено нод: {Nodes.Count}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось загрузить список нод: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Asks the user's own running keryxd (must be up, with --rpclisten-json - always
    /// added by NodeArgumentBuilder now) for its real currently-known peers and adds them to the
    /// list, clearly labeled as RPC-discovered (see OwnNodePeerDiscoveryService's doc comment).</summary>
    [RelayCommand]
    private async Task DiscoverFromOwnNodeAsync()
    {
        IsBusy = true;
        try
        {
            var (host, port) = OwnNodeRpcAddress();
            var discovered = await _discoveryService.DiscoverPeersAsync(host, port, _profile.UseTestnet, CancellationToken.None);
            var addedCount = 0;
            foreach (var node in discovered)
            {
                if (Nodes.Any(n => n.Info.Endpoint == node.Endpoint)) continue;
                Nodes.Add(new PublicNodeRowViewModel(node));
                addedCount++;
            }
            StatusMessage = addedCount == 0
                ? "Ваша нода не сообщила новых пиров (возможно, она ещё не подключена ни к кому, или список уже содержит их)."
                : $"Добавлено {addedCount} пир(ов), обнаруженных через RPC вашей ноды.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Не удалось получить пиров от своей ноды - убедитесь, что она запущена. " +
                             $"({ex.Message})";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PingAsync(PublicNodeRowViewModel row)
    {
        row.IsChecking = true;
        try
        {
            var result = await PublicNodeDirectoryService.CheckHealthAsync(row.Info, TimeSpan.FromSeconds(3));
            var uptimeNote = row.Info.SelfReportedUptimePercent is double uptime
                ? $" · аптайм по заявлению оператора: {uptime:0.#}%"
                : "";
            row.StatusText = result.Reachable
                ? $"В сети · {result.LatencyMs:0} мс{uptimeNote}"
                : $"Недоступна ({result.Error}){uptimeNote}";
        }
        finally
        {
            row.IsChecking = false;
        }
    }

    [RelayCommand]
    private void UseNode(PublicNodeRowViewModel row)
    {
        // Remember the profile's own node settings before overwriting them, but only the first
        // time (if the user bounces between several substitute nodes, keep the ORIGINAL own-node
        // values, not the previous substitute's).
        if (_syncWatchCts is null)
        {
            _rememberedOwnEndpoint = _profile.NodeEndpoint;
            _rememberedOwnPort = _profile.NodePort;
            _rememberedAutoStartNode = _profile.AutoStartNode;
        }

        _profile.NodeEndpoint = row.Info.Endpoint;
        _profile.NodePort = row.Info.Port;
        // Using someone else's node means this app shouldn't also try to launch a redundant local
        // one - AutoStartMiner is deliberately left untouched, only the node side changes.
        _profile.AutoStartNode = false;
        _persist();
        StatusMessage = $"Нода «{row.Info.Name}» ({row.Info.Endpoint}:{row.Info.Port}) выбрана для подключения. " +
                         "Автозапуск локальной ноды отключён - её всё ещё можно включить обратно на этой странице.";

        StartWatchingForOwnNodeSync();
    }

    /// <summary>Manual override in case the user wants to switch back before the automatic
    /// sync-watch fires (or if it was never started, e.g. the app was restarted after UseNode was
    /// called in a previous session).</summary>
    [RelayCommand]
    private void SwitchBackToOwnNode()
    {
        StopWatchingForOwnNodeSync();
        _profile.NodeEndpoint = _rememberedOwnEndpoint ?? "127.0.0.1";
        _profile.NodePort = _rememberedOwnPort;
        _profile.AutoStartNode = _rememberedAutoStartNode || true;
        _persist();
        StatusMessage = "Возвращено подключение к собственной ноде.";
    }

    /// <summary>Called by NodeViewModel when the active profile changes or the app is shutting
    /// down, so a stale watch loop (polling the PREVIOUS profile's own-node RPC port) doesn't keep
    /// running against the wrong profile. Known limitation: if the app is closed entirely while a
    /// watch is active, the loop simply ends with the process - no persisted "resume watching"
    /// state exists across restarts, matching this profile field's own transient nature.</summary>
    public void StopWatching() => StopWatchingForOwnNodeSync();

    private void StartWatchingForOwnNodeSync()
    {
        StopWatchingForOwnNodeSync();
        _syncWatchCts = new CancellationTokenSource();
        IsWatchingForOwnNodeSync = true;
        _ = WatchLoopAsync(_syncWatchCts.Token);
    }

    private void StopWatchingForOwnNodeSync()
    {
        _syncWatchCts?.Cancel();
        _syncWatchCts?.Dispose();
        _syncWatchCts = null;
        IsWatchingForOwnNodeSync = false;
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(SyncPollInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                bool synced;
                try
                {
                    var (host, port) = OwnNodeRpcAddress();
                    synced = await _discoveryService.GetIsSyncedAsync(host, port, ct);
                }
                catch
                {
                    // Own node not reachable yet (still starting, or RPC not up) - keep polling
                    // rather than giving up; this is expected while the local keryxd is mid-launch.
                    continue;
                }

                if (synced)
                {
                    _profile.NodeEndpoint = _rememberedOwnEndpoint ?? "127.0.0.1";
                    _profile.NodePort = _rememberedOwnPort;
                    _profile.AutoStartNode = true;
                    _persist();
                    StatusMessage = "Ваша нода синхронизировалась - выполнено автоматическое переключение обратно на неё.";
                    IsWatchingForOwnNodeSync = false;
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by StopWatchingForOwnNodeSync/SwitchBackToOwnNode - not an error.
        }
    }

    /// <summary>The RPC address to poll the user's OWN node at - always 127.0.0.1 (this app only
    /// ever launches keryxd locally), never the currently-active substitute NodeEndpoint (which
    /// would defeat the whole point of watching for the user's own node to finish syncing).</summary>
    private (string Host, int Port) OwnNodeRpcAddress()
    {
        var port = _profile.NodeRpcJsonPort
            ?? (_profile.UseTestnet
                ? NodeArgumentBuilder.DefaultRpcJsonPortTestnet
                : NodeArgumentBuilder.DefaultRpcJsonPortMainnet);
        return ("127.0.0.1", port);
    }
}
