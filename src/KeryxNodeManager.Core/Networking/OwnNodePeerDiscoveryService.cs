namespace KeryxNodeManager.Core.Networking;

/// <summary>
/// Queries the user's OWN running keryxd (via <see cref="KeryxRpcJsonClient"/>, real RPC calls
/// confirmed against the actual keryx-node source - see that class's doc comment) for its
/// currently-connected peers and known peer addresses, and its own sync status.
///
/// This supersedes half of <see cref="PublicNodeInfo"/>'s original doc comment, which said the
/// node list is "necessarily curated by hand... not discovered" - that was true when this class
/// didn't exist (no RPC method was known to be reachable), but keryxd DOES expose
/// `getConnectedPeerInfo`/`getPeerAddresses` once launched with `--rpclisten-json` (which
/// NodeArgumentBuilder now always adds). Entries this class returns are honestly labeled via
/// <see cref="PublicNodeInfo.Notes"/> as "discovered from your own node," distinct from the
/// hand-curated bundled/remote-JSON entries <see cref="PublicNodeDirectoryService"/> produces -
/// callers should never merge these two silently without keeping that provenance visible, since
/// "a peer my node is talking to" and "an operator who submitted their address to a hosted JSON
/// file" carry very different trust/verification signals.
///
/// A peer address on its own (e.g. from getPeerAddresses) is NOT the same thing as an RPC endpoint
/// this app's miner could point at - Keryx/Kaspa peer-to-peer ports are for block/tx relay, not
/// necessarily wRPC. This class deliberately only surfaces peers as candidates using keryxd's own
/// gRPC/wRPC listen port convention (mainnet 22110/testnet 22210 for gRPC, which is what
/// PublicNodeDirectoryService.CheckHealthAsync's plain TCP probe already assumes elsewhere in this
/// app) - whether that port is actually open on the *peer's* firewall is unknown until pinged, same
/// as any hand-curated entry.
/// </summary>
public sealed class OwnNodePeerDiscoveryService
{
    /// <summary>Real gRPC listen port convention confirmed via keryxd --help (same numbers already
    /// used for the app's own TCP reachability probe on the Node page).</summary>
    private const int MainnetGrpcPort = 22110;
    private const int TestnetGrpcPort = 22210;

    public async Task<bool> GetIsSyncedAsync(string rpcHost, int rpcPort, CancellationToken ct)
    {
        await using var client = new KeryxRpcJsonClient();
        await client.ConnectAsync(rpcHost, rpcPort, ct);
        var info = await client.GetServerInfoAsync(ct);
        return info.IsSynced;
    }

    public async Task<IReadOnlyList<PublicNodeInfo>> DiscoverPeersAsync(
        string rpcHost, int rpcPort, bool useTestnet, CancellationToken ct)
    {
        await using var client = new KeryxRpcJsonClient();
        await client.ConnectAsync(rpcHost, rpcPort, ct);

        var results = new List<PublicNodeInfo>();
        var grpcPort = useTestnet ? TestnetGrpcPort : MainnetGrpcPort;

        var connected = await client.GetConnectedPeerInfoAsync(ct);
        foreach (var peer in connected.PeerInfo)
        {
            var (ip, _) = SplitAddress(peer.Address);
            if (ip is null) continue;
            results.Add(new PublicNodeInfo(
                Name: $"Пир вашей ноды ({(string.IsNullOrEmpty(peer.UserAgent) ? ip : peer.UserAgent)})",
                Endpoint: ip,
                Port: grpcPort,
                Region: null,
                Notes: "Обнаружен через RPC вашей ноды (getConnectedPeerInfo) - подключён к вам прямо сейчас."));
        }

        var known = await client.GetPeerAddressesAsync(ct);
        foreach (var addr in known.KnownAddresses)
        {
            if (string.IsNullOrWhiteSpace(addr.Ip)) continue;
            if (results.Any(r => r.Endpoint == addr.Ip)) continue; // already listed as connected
            results.Add(new PublicNodeInfo(
                Name: $"Известный адрес вашей ноды ({addr.Ip})",
                Endpoint: addr.Ip!,
                Port: grpcPort,
                Region: null,
                Notes: "Обнаружен через RPC вашей ноды (getPeerAddresses) - известен, но не обязательно подключён сейчас."));
        }

        return results;
    }

    /// <summary>keryxd's RpcPeerAddress serializes as "ip:port" (NetAddress's Display impl per
    /// Kaspa convention) - split defensively rather than assume no IPv6 brackets.</summary>
    private static (string? Ip, int? Port) SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return (null, null);
        var lastColon = address.LastIndexOf(':');
        if (lastColon <= 0) return (address, null);
        var ipPart = address[..lastColon].Trim('[', ']');
        var portPart = address[(lastColon + 1)..];
        return int.TryParse(portPart, out var port) ? (ipPart, port) : (ipPart, null);
    }
}
