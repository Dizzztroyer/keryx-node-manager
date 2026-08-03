namespace KeryxNodeManager.Core.Networking;

/// <summary>
/// One entry in a public/community Keryx node directory (brief-adjacent request: "let the miner
/// point at someone else's node if mine isn't ready/synced yet"). Entries come from two genuinely
/// different sources, both real, neither fabricated:
///
/// 1. Hand-curated: the bundled/remote-JSON list loaded by <see cref="PublicNodeDirectoryService"/>
///    - an operator chose to list their node in a JSON file this app fetches. No protocol-level
///    verification backs these beyond the app's own reachability probe.
/// 2. RPC-discovered: <see cref="OwnNodePeerDiscoveryService"/> queries the user's own running
///    keryxd (confirmed real `getConnectedPeerInfo`/`getPeerAddresses` RPC calls, once launched
///    with `--rpclisten-json` - see NodeArgumentBuilder) for peers it already knows about. This
///    was NOT possible in earlier sessions (no RPC method was confirmed reachable at the time),
///    hence the original, now-outdated assumption that this list was necessarily hand-curated only.
///
/// Callers must keep <see cref="Notes"/> visible so the user can tell which kind of entry they're
/// looking at - "an operator listed this node in a public JSON file" and "my own node is currently
/// talking to this peer" carry very different trust signals.
///
/// <c>SelfReportedUptimePercent</c> is exactly what its name says: a number the node operator
/// chose to put in the JSON, not something this app measured. See
/// <see cref="PublicNodeHealthResult"/> for the one honest, locally-measurable signal this app
/// actually produces itself.
/// </summary>
public sealed record PublicNodeInfo(
    string Name,
    string Endpoint,
    int Port,
    string? Region = null,
    string? Notes = null,
    double? SelfReportedUptimePercent = null);

/// <summary>
/// The result of this app's own reachability probe against one <see cref="PublicNodeInfo"/> - a
/// single point-in-time TCP-connect timing, not a historical uptime percentage. A desktop app that
/// only runs when the user has it open cannot honestly claim to know a remote node's uptime over
/// time (that needs a continuously-running monitor, which this app is not) - showing "reachable
/// just now, Nms" is the most this class will ever claim, and callers/UI must not relabel that as
/// "uptime" without it being clearly attributed as self-reported by the operator instead.
/// </summary>
public sealed record PublicNodeHealthResult(bool Reachable, double? LatencyMs, DateTimeOffset CheckedAt, string? Error);
