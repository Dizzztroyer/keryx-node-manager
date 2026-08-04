using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.Cli;

/// <summary>Builds argv for keryxd.exe. Same ArgumentList-only rule as MinerArgumentBuilder.</summary>
public static class NodeArgumentBuilder
{
    /// <summary>keryxd's own documented defaults (--help), confirmed against the real
    /// keryxd/src/args.rs source rather than guessed: gRPC listens by default without any flag,
    /// but the wRPC JSON listener (--rpclisten-json) is off unless explicitly requested.</summary>
    public const int DefaultRpcJsonPortMainnet = 24110;
    public const int DefaultRpcJsonPortTestnet = 24210;

    public static List<string> Build(MiningProfile profile, string? appDataDir)
    {
        var args = new List<string>();

        if (profile.UseTestnet)
        {
            args.Add("--testnet");
        }

        // Prefer the explicit appDataDir parameter (caller's override) but fall back to the
        // profile's own NodeDataDirectory - see MiningProfile.NodeDataDirectory's doc comment for
        // why this was previously always empty in practice.
        var effectiveDataDir = string.IsNullOrWhiteSpace(appDataDir) ? profile.NodeDataDirectory : appDataDir;
        if (!string.IsNullOrWhiteSpace(effectiveDataDir))
        {
            args.Add("--appdir");
            args.Add(effectiveDataDir);
        }

        if (profile.NodeRpcJsonEnabled)
        {
            // Always bind to loopback only - this app's own RPC client never needs, and should
            // never request, a listener reachable from outside this machine.
            var port = profile.NodeRpcJsonPort
                ?? (profile.UseTestnet ? DefaultRpcJsonPortTestnet : DefaultRpcJsonPortMainnet);
            args.Add($"--rpclisten-json=127.0.0.1:{port}");
        }

        if (profile.NodeUtxoIndexEnabled)
        {
            args.Add("--utxoindex");
        }

        foreach (var extra in profile.ExtraNodeArguments)
        {
            if (!string.IsNullOrWhiteSpace(extra))
            {
                args.Add(extra);
            }
        }

        return args;
    }
}
