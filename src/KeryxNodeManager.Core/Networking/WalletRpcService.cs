namespace KeryxNodeManager.Core.Networking;

/// <summary>
/// Queries the user's OWN running keryxd (same RPC transport/connection pattern as
/// <see cref="OwnNodePeerDiscoveryService"/>: one KeryxRpcJsonClient per call, connect/dispose,
/// nothing cached) for a single public address's balance and currently-unspent outputs. This is
/// deliberately the safe half of "wallet" functionality: everything it needs is the public
/// `keryx:...` mining address already stored in MiningProfile.MiningAddress - no private key, seed
/// phrase, or signing capability is read, held, or even touched anywhere in this class or its
/// caller. It cannot spend anything; it can only ask the node "what does this address currently
/// hold."
///
/// Both RPC calls require keryxd to have been launched with --utxoindex (see
/// MiningProfile.NodeUtxoIndexEnabled/NodeArgumentBuilder) - if it wasn't, keryxd itself rejects
/// the call with its own error text, which this class lets propagate as a KeryxRpcException
/// unmodified rather than guessing at a friendlier message (same "let the node be authoritative"
/// convention as KeryxAddressValidator).
///
/// "Recent activity" here means unspent outputs (UTXOs), not a transaction ledger - keryxd's RPC
/// surface has no separate address-history call (confirmed against the real RpcApiOps enum), and
/// once a UTXO is spent it simply stops appearing in this list. Sorted newest-first by
/// blockDaaScore (Keryx/Kaspa's monotonic block-ordering counter - not a wall-clock timestamp, but
/// the closest ordering signal this RPC surface actually returns) and capped at
/// <paramref name="maxEntries"/> so a heavily-used address doesn't flood the Dashboard.
/// </summary>
public sealed class WalletRpcService
{
    public async Task<WalletSnapshot> GetSnapshotAsync(
        string rpcHost, int rpcPort, string address, int maxEntries, CancellationToken ct)
    {
        await using var client = new KeryxRpcJsonClient();
        await client.ConnectAsync(rpcHost, rpcPort, ct);

        var balanceResponse = await client.GetBalanceByAddressAsync(address, ct);
        var utxoResponse = await client.GetUtxosByAddressesAsync(new[] { address }, ct);

        var entries = utxoResponse.Entries
            .Where(e => e.UtxoEntry is not null)
            .OrderByDescending(e => e.UtxoEntry!.BlockDaaScore)
            .Take(maxEntries)
            .Select(e => new WalletUtxoSummary(
                AmountSompi: e.UtxoEntry!.Amount,
                IsCoinbase: e.UtxoEntry!.IsCoinbase,
                BlockDaaScore: e.UtxoEntry!.BlockDaaScore,
                TransactionId: e.Outpoint?.TransactionId ?? ""))
            .ToList();

        return new WalletSnapshot(balanceResponse.Balance, entries);
    }
}

/// <summary>Sompi is the smallest unit (1 KRX = 100,000,000 sompi, same base unit as this
/// codebase's Kaspa ancestor - confirmed via docs/KERYX_RESEARCH.md's stats-server JSON field
/// names "claimed_sompi"/"escrow_pending_sompi"). Never pre-divided here; the ViewModel/View layer
/// formats for display so this Core-layer type stays a plain data carrier.</summary>
public sealed record WalletSnapshot(ulong BalanceSompi, IReadOnlyList<WalletUtxoSummary> RecentEntries);

public sealed record WalletUtxoSummary(ulong AmountSompi, bool IsCoinbase, ulong BlockDaaScore, string TransactionId);
