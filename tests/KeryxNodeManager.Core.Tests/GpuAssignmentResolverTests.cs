using KeryxNodeManager.Core.ModelAssignment;
using KeryxNodeManager.Core.Models;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// GpuAssignmentResolver is the single place that turns a MiningProfile's persisted per-GPU
/// choices into the CUDA-ordered ModelTier? list + anyManualOverride flag MinerArgumentBuilder
/// consumes - both the Dashboard's real launch and the Miner page's advanced-mode preview go
/// through it, so a bug here would silently desync what the user sees from what actually runs.
/// </summary>
public class GpuAssignmentResolverTests
{
    private readonly TierAssigner _assigner = new();

    private static GpuDevice Gpu(string uuid, int cudaIndex, long totalMb) => new()
    {
        Uuid = uuid,
        CudaIndex = cudaIndex,
        Name = $"GPU {cudaIndex}",
        TotalVramMb = totalMb,
    };

    [Fact]
    public void AllAuto_NoAssignmentsSaved_ProducesNoManualOverride()
    {
        var devices = new[] { Gpu("A", 0, 24_600), Gpu("B", 1, 8_600) };
        var profile = new MiningProfile(); // GpuAssignments empty

        var (assignments, anyManualOverride) = GpuAssignmentResolver.Resolve(devices, profile, _assigner);

        Assert.False(anyManualOverride);
        Assert.Equal(2, assignments.Count);
        Assert.Equal(ModelTier.High, assignments[0]);   // CUDA index 0, 24.6GB -> High
        Assert.Equal(ModelTier.Light, assignments[1]);  // CUDA index 1, 8.6GB -> Light
    }

    [Fact]
    public void ResultIsOrderedByCudaIndex_NotByInputOrder()
    {
        // Deliberately pass devices out of CUDA order - resolver must still emit them index 0..N.
        var devices = new[] { Gpu("B", 1, 8_600), Gpu("A", 0, 24_600) };
        var profile = new MiningProfile();

        var (assignments, _) = GpuAssignmentResolver.Resolve(devices, profile, _assigner);

        Assert.Equal(ModelTier.High, assignments[0]);  // CUDA 0 = "A"
        Assert.Equal(ModelTier.Light, assignments[1]); // CUDA 1 = "B"
    }

    [Fact]
    public void ManualTierOverride_SetsAnyManualOverrideTrue()
    {
        var devices = new[] { Gpu("A", 0, 24_600) };
        var profile = new MiningProfile
        {
            GpuAssignments = { new GpuAssignment { GpuUuid = "A", Mode = nameof(ModelTier.VeryLight) } },
        };

        var (assignments, anyManualOverride) = GpuAssignmentResolver.Resolve(devices, profile, _assigner);

        Assert.True(anyManualOverride);
        Assert.Equal(ModelTier.VeryLight, assignments[0]);
    }

    [Fact]
    public void DisabledGpu_YieldsNullTierAndCountsAsManualOverride()
    {
        var devices = new[] { Gpu("A", 0, 24_600), Gpu("B", 1, 24_600) };
        var profile = new MiningProfile
        {
            GpuAssignments = { new GpuAssignment { GpuUuid = "B", Mode = GpuAssignmentMode.Disabled } },
        };

        var (assignments, anyManualOverride) = GpuAssignmentResolver.Resolve(devices, profile, _assigner);

        Assert.True(anyManualOverride);
        Assert.Equal(ModelTier.High, assignments[0]); // A stays Auto
        Assert.Null(assignments[1]);                  // B explicitly disabled
    }

    [Fact]
    public void AutoAssignmentThatDisablesAGpu_DoesNotCountAsManualOverride()
    {
        // A card too small for even the lightest tier gets Disabled by AssignAuto itself - this
        // must NOT set anyManualOverride, since the miner's own auto-fit would reach the same
        // conclusion unassisted (docs/KERYX_RESEARCH.md §2-3).
        var devices = new[] { Gpu("A", 0, 2_000) };
        var profile = new MiningProfile(); // no explicit assignment - stays "auto"

        var (assignments, anyManualOverride) = GpuAssignmentResolver.Resolve(devices, profile, _assigner);

        Assert.False(anyManualOverride);
        Assert.Null(assignments[0]);
    }

    [Fact]
    public void UnknownPersistedModeString_FailsSafeToAuto()
    {
        // Simulates a profile saved by a future version with a tier name that doesn't exist here.
        var devices = new[] { Gpu("A", 0, 24_600) };
        var profile = new MiningProfile
        {
            GpuAssignments = { new GpuAssignment { GpuUuid = "A", Mode = "SomeFutureTierName" } },
        };

        var (assignments, anyManualOverride) = GpuAssignmentResolver.Resolve(devices, profile, _assigner);

        // Falls back to what AssignAuto would pick, and (per the "no manual override" contract
        // for auto-derived results) does not force --force-model just because of the fallback.
        Assert.Equal(ModelTier.High, assignments[0]);
        Assert.True(anyManualOverride); // the profile DID contain a non-"auto" mode string, so
                                        // this is intentionally still true - it's an override
                                        // attempt that failed validation, not a silent no-op.
    }
}
