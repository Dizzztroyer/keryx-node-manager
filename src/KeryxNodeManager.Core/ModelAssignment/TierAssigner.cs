using KeryxNodeManager.Core.Localization;
using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.ModelAssignment;

public sealed record TierAssignmentResult(
    string GpuUuid,
    ModelTier? Tier,
    bool Disabled,
    bool ForcedBelowRecommendedVram,
    string Explanation);

/// <summary>
/// Implements the Auto model-assignment described in brief §6: pick the highest tier a GPU's
/// VRAM can safely hold, reserving headroom for whatever the OS/other processes already use.
/// This never combines VRAM across GPUs — every decision is per-card, matching
/// docs/KERYX_RESEARCH.md §3 ("no VRAM pooling").
/// </summary>
public sealed class TierAssigner
{
    /// <summary>
    /// MB of VRAM to keep free beyond a tier's min_vram_mb before auto-assigning it, so a card
    /// that is borderline (e.g. already has other software using VRAM) doesn't get pushed into an
    /// OOM on first launch. This is a UX safety margin chosen by this app, not a Keryx-sourced
    /// number — the miner's own min_vram_mb already includes its own KV-cache/workspace margin
    /// per docs/KERYX_RESEARCH.md §3.
    /// </summary>
    public const long SafetyMarginMb = 500;

    public TierAssignmentResult AssignAuto(GpuDevice gpu)
    {
        long availableMb = Math.Max(0, gpu.TotalVramMb - gpu.UsedVramMb);
        var best = ModelTierCatalog.ByDescendingVram()
            .FirstOrDefault(t => availableMb >= t.MinVramMb + SafetyMarginMb);

        if (best is null)
        {
            // Even the smallest tier doesn't fit with margin - try without margin, matching
            // the miner's own fallback behaviour ("falls back to a tier it can actually serve").
            best = ModelTierCatalog.ByDescendingVram().FirstOrDefault(t => availableMb >= t.MinVramMb);
        }

        if (best is null)
        {
            return new TierAssignmentResult(
                gpu.Uuid, Tier: null, Disabled: true, ForcedBelowRecommendedVram: false,
                Explanation: CoreStrings.Format("Tier.ExcludedInsufficientVram",
                    gpu.Name, availableMb, ModelTierCatalog.Tiers.Min(t => t.MinVramMb)));
        }

        return new TierAssignmentResult(
            gpu.Uuid, best.Tier, Disabled: false, ForcedBelowRecommendedVram: false,
            Explanation: CoreStrings.Format("Tier.AutoAssigned", gpu.Name, availableMb, best.Name, best.MinVramMb));
    }

    /// <summary>
    /// Validates a user's manual tier choice against a GPU's VRAM. Does not block the choice
    /// (the brief allows forcing it with a confirmation) but flags whether it's below the
    /// recommended minimum so the UI can show a warning before launch.
    /// </summary>
    public TierAssignmentResult AssignManual(GpuDevice gpu, ModelTier tier)
    {
        var spec = ModelTierCatalog.Get(tier);
        long availableMb = Math.Max(0, gpu.TotalVramMb - gpu.UsedVramMb);
        bool underMinimum = availableMb < spec.MinVramMb;

        string explanation = underMinimum
            ? CoreStrings.Format("Tier.ManualRisky", gpu.Name, spec.Name, spec.MinVramMb, availableMb)
            : CoreStrings.Format("Tier.ManualFits", gpu.Name, spec.Name, availableMb);

        return new TierAssignmentResult(gpu.Uuid, tier, Disabled: false, underMinimum, explanation);
    }
}
