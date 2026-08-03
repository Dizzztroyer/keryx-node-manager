using System.Globalization;
using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.Cli;

/// <summary>
/// Builds the argv for keryx-miner.exe as a List&lt;string&gt; — this is deliberately never
/// concatenated into a single shell command string. Callers pass this list straight into
/// ProcessStartInfo.ArgumentList, so a mining address, extra argument, or file path containing
/// spaces/quotes/&amp;/| cannot break out into a second command (brief §9, §20: "not cmd /c or
/// shell concatenation", "protect from command injection").
/// </summary>
public static class MinerArgumentBuilder
{
    /// <summary>
    /// orderedGpuAssignments must be in CUDA driver order (index 0..N) — that is the order
    /// --force-model's CSV list is positionally interpreted in by keryx-miner
    /// (docs/KERYX_RESEARCH.md §2). A null tier in a slot means "disabled" and is represented by
    /// omitting that GPU from CUDA_VISIBLE_DEVICES rather than passing a bogus tier name.
    /// </summary>
    public static List<string> Build(
        MiningProfile profile,
        IReadOnlyList<ModelTier?> orderedGpuAssignments,
        bool anyManualOverride)
    {
        if (string.IsNullOrWhiteSpace(profile.MiningAddress))
            throw new InvalidOperationException("mining address is required to build miner arguments");

        var args = new List<string>
        {
            "--mining-address", profile.MiningAddress,
            "--keryxd-address", profile.NodeEndpoint,
        };

        if (profile.NodePort is int port)
        {
            args.Add("--port");
            args.Add(port.ToString(CultureInfo.InvariantCulture));
        }

        if (profile.UseTestnet)
        {
            args.Add("--testnet");
        }

        if (!string.IsNullOrWhiteSpace(profile.ModelsDirectory))
        {
            args.Add("--models-dir");
            args.Add(profile.ModelsDirectory);
        }

        if (!string.IsNullOrWhiteSpace(profile.IpfsUrl))
        {
            args.Add("--ipfs-url");
            args.Add(profile.IpfsUrl);
        }

        if (profile.DevFundPercent != 2.0m)
        {
            args.Add($"--devfund-percent={profile.DevFundPercent.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        // --force-model is only emitted when at least one GPU has a manual override or a disabled
        // GPU needs excluding from the driver-order list; pure "all Auto" launches omit it
        // entirely and let the miner apply its own per-card VRAM auto-fit, matching stock
        // behaviour exactly (docs/KERYX_RESEARCH.md §2-3).
        if (anyManualOverride)
        {
            var tokens = orderedGpuAssignments
                .Select(t => t is null ? null : ModelTierCatalog.ForceModelToken(t.Value))
                .Where(t => t is not null);
            var joined = string.Join(",", tokens);
            if (joined.Length > 0)
            {
                args.Add("--force-model");
                args.Add(joined);
            }
        }

        foreach (var extra in profile.ExtraMinerArguments)
        {
            if (!string.IsNullOrWhiteSpace(extra))
            {
                args.Add(extra);
            }
        }

        return args;
    }

    /// <summary>
    /// CUDA_VISIBLE_DEVICES value to exclude Disabled GPUs from the process entirely, using the
    /// CUDA-index positions of the GPUs the profile keeps enabled. This is standard CUDA-driver
    /// behaviour (respected by any CUDA app, including keryx-miner) — it is NOT something
    /// keryx-miner's own source implements or documents (docs/KERYX_RESEARCH.md §2/§7), so treat
    /// it as best-effort: if it does not take effect for some future miner build, --force-model
    /// combined with a "disabled" placeholder is the fallback.
    /// </summary>
    public static string BuildCudaVisibleDevices(IEnumerable<int> enabledCudaIndexesInOrder) =>
        string.Join(",", enabledCudaIndexesInOrder);
}
