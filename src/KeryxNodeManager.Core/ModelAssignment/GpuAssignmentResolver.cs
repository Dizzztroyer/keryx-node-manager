using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.ModelAssignment;

/// <summary>
/// Turns a MiningProfile's persisted per-GPU choices (brief §6: Auto/Manual tier/Disabled, keyed
/// by GPU UUID) into the CUDA-driver-ordered ModelTier? list MinerArgumentBuilder.Build expects,
/// plus the anyManualOverride flag that decides whether --force-model is emitted at all.
///
/// This is the single place that logic lives so the GPU page's live preview, the Miner page's
/// advanced-mode command preview, and the actual Dashboard launch path can never disagree with
/// each other about what will be passed to keryx-miner.exe (previously each of those built its
/// own "all Auto, no override" placeholder independently - see PROJECT_STATUS.md).
/// </summary>
public static class GpuAssignmentResolver
{
    public static (IReadOnlyList<ModelTier?> Assignments, bool AnyManualOverride) Resolve(
        IReadOnlyList<GpuDevice> devices,
        MiningProfile profile,
        TierAssigner tierAssigner)
    {
        // CUDA driver order (index 0..N) is what --force-model's CSV list is positionally
        // interpreted against (docs/KERYX_RESEARCH.md §2) - never the order devices happened to
        // be enumerated in, and never a UI display order.
        var ordered = devices.OrderBy(d => d.CudaIndex).ToList();

        var assignments = new List<ModelTier?>(ordered.Count);
        bool anyManualOverride = false;

        foreach (var device in ordered)
        {
            var saved = profile.GpuAssignments.FirstOrDefault(a => a.GpuUuid == device.Uuid);
            var mode = saved?.Mode ?? GpuAssignmentMode.Auto;

            if (mode != GpuAssignmentMode.Auto)
            {
                // A user explicitly chose Disabled or a specific tier for at least one GPU - this
                // is what triggers --force-model. A GPU that Auto-assignment itself decides to
                // disable (insufficient VRAM even for the lightest tier) does NOT count: the
                // miner's own auto-fit would reach the same conclusion unassisted, so there is no
                // need to force anything for it (docs/KERYX_RESEARCH.md §2-3).
                anyManualOverride = true;
            }

            ModelTier? tier = mode switch
            {
                GpuAssignmentMode.Auto => tierAssigner.AssignAuto(device).Tier,
                GpuAssignmentMode.Disabled => null,
                _ => Enum.TryParse<ModelTier>(mode, out var parsed)
                    ? parsed
                    : tierAssigner.AssignAuto(device).Tier, // unknown mode string: fail safe to Auto
            };

            assignments.Add(tier);
        }

        return (assignments, anyManualOverride);
    }
}
