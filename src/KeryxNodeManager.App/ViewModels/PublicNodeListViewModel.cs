using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
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
///
/// Also new: transitive discovery. "Найти пиров через эту ноду" (DiscoverThroughNodeAsync) runs the
/// exact same RPC discovery as DiscoverFromOwnNodeAsync, but against ANY node already in the list
/// (bundled, remote, or discovered) instead of only 127.0.0.1 - so node A's peers can themselves be
/// asked for their own peers, chaining outward. This was verified for real against this project's
/// own synced node's log (real peer IPs pulled from keryx.log, then TCP-probed on the gRPC port):
/// most peers do NOT have their RPC/gRPC port open externally (only the P2P port is expected to
/// be), so each hop's yield drops off - this is a real, honest limitation, not a bug. A node
/// successfully verified this way (PingAsync confirms its RPC port is actually reachable) is cached
/// to disk (see <see cref="_discoveredCachePath"/>) so the list keeps growing across restarts on
/// THIS machine only - deliberately NOT synced to other users/installs automatically, since blindly
/// trusting addresses submitted by arbitrary other installations (with no moderation) would be a
/// real abuse vector (a malicious actor could seed fake/malicious node addresses that everyone's
/// miner then silently trusts). A shared, moderated community list (e.g. reviewed PRs to this
/// repo's bundled JSON) is the safer path for cross-user sharing, not automatic sync.
/// </summary>
public partial class PublicNodeListViewModel : ObservableObject
{
    private static readonly TimeSpan SyncPollInterval = TimeSpan.FromSeconds(20);

    private readonly PublicNodeDirectoryService _directoryService;
    private readonly OwnNodePeerDiscoveryService _discoveryService;
    private readonly MiningProfile _profile;
    private readonly Action _persist;
    /// <summary>Path to a small local JSON cache of RPC-verified discovered nodes (see this class's
    /// doc comment) - null disables persistence entirely (e.g. in tests). Deliberately App-layer
    /// only: Core has no concept of "this machine's local cache file," matching every other
    /// per-machine file path in this app (ConfigStore, LogSink).</summary>
    private readonly string? _discoveredCachePath;

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
        Action persist,
        string? discoveredCachePath = null)
    {
        _directoryService = directoryService;
        _discoveryService = discoveryService;
        _profile = profile;
        _persist = persist;
        _discoveredCachePath = discoveredCachePath;
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

            var cached = LoadDiscoveredCache();

            foreach (var node in bundled.Concat(remote).Concat(cached))
            {
                if (Nodes.Any(n => n.Info.Endpoint == node.Endpoint)) continue; // cache can overlap bundled
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

    /// <summary>Same RPC discovery as DiscoverFromOwnNodeAsync, but pointed at an arbitrary node
    /// already in the list instead of the user's own 127.0.0.1 - see this class's doc comment for
    /// the transitive-discovery reasoning and its real, honest yield limitation.</summary>
    [RelayCommand]
    private async Task DiscoverThroughNodeAsync(PublicNodeRowViewModel row)
    {
        IsBusy = true;
        try
        {
            var discovered = await _discoveryService.DiscoverPeersAsync(
                row.Info.Endpoint, row.Info.Port, _profile.UseTestnet, CancellationToken.None);
            var addedCount = 0;
            foreach (var node in discovered)
            {
                if (Nodes.Any(n => n.Info.Endpoint == node.Endpoint)) continue;
                Nodes.Add(new PublicNodeRowViewModel(node));
                addedCount++;
            }
            StatusMessage = addedCount == 0
                ? $"Нода «{row.Info.Name}» не сообщила новых пиров, или её RPC-порт недоступен."
                : $"Добавлено {addedCount} пир(ов), обнаруженных через RPC ноды «{row.Info.Name}».";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось получить пиров от «{row.Info.Name}» - её RPC-порт, скорее всего, " +
                             $"закрыт наружу (это нормально, большинство операторов его не открывают). ({ex.Message})";
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

            // Only ever persist nodes that came from RPC discovery (see PublicNodeInfo.Notes
            // provenance convention) AND were just confirmed reachable by this app's own probe -
            // bundled/remote-JSON entries are already persisted by their source and don't need a
            // second local copy, and an unreachable discovered node isn't worth remembering.
            if (result.Reachable && row.Info.Notes?.Contains("Обнаружен", StringComparison.Ordinal) == true)
            {
                PersistDiscoveredNode(row.Info);
            }
        }
        finally
        {
            row.IsChecking = false;
        }
    }

    /// <summary>Loads the local discovered-node cache from disk - missing file or any parse error
    /// is treated as "empty cache," never a hard failure, since this is purely a convenience
    /// accumulation and not required for the app to function.</summary>
    private List<PublicNodeInfo> LoadDiscoveredCache()
    {
        if (string.IsNullOrEmpty(_discoveredCachePath) || !File.Exists(_discoveredCachePath))
        {
            return new List<PublicNodeInfo>();
        }
        try
        {
            var json = File.ReadAllText(_discoveredCachePath);
            return JsonSerializer.Deserialize<List<PublicNodeInfo>>(json) ?? new List<PublicNodeInfo>();
        }
        catch
        {
            return new List<PublicNodeInfo>();
        }
    }

    private void PersistDiscoveredNode(PublicNodeInfo info)
    {
        if (string.IsNullOrEmpty(_discoveredCachePath)) return;
        try
        {
            var existing = LoadDiscoveredCache();
            if (existing.Any(n => n.Endpoint == info.Endpoint)) return; // already cached
            existing.Add(info with
            {
                Notes = (info.Notes ?? "") + " Сохранено локально после успешной проверки доступности.",
            });
            var dir = Path.GetDirectoryName(_discoveredCachePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_discoveredCachePath, JsonSerializer.Serialize(existing));
        }
        catch
        {
            // Best-effort convenience cache - a write failure (disk full, permissions) should never
            // break the ping/discovery flow the user actually cares about.
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
