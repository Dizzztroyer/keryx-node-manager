using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.ModelAssignment;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

public class TierAssignerTests
{
    private readonly TierAssigner _assigner = new();

    private static GpuDevice Gpu(long totalMb, long usedMb = 0, string name = "Test GPU") => new()
    {
        Uuid = "GPU-test",
        CudaIndex = 0,
        Name = name,
        TotalVramMb = totalMb,
        UsedVramMb = usedMb,
    };

    [Theory]
    [InlineData(6_000, ModelTier.VeryLight)]   // exactly at floor, minus margin -> below very-light with margin, falls back
    [InlineData(6_600, ModelTier.VeryLight)]
    [InlineData(8_600, ModelTier.Light)]
    [InlineData(12_600, ModelTier.Default)]
    [InlineData(24_600, ModelTier.High)]
    [InlineData(30_600, ModelTier.VeryHigh)]
    [InlineData(48_000, ModelTier.VeryHigh)]
    public void AssignAuto_PicksHighestTierThatFitsWithMargin(long totalMb, ModelTier expected)
    {
        var result = _assigner.AssignAuto(Gpu(totalMb));
        Assert.False(result.Disabled);
        Assert.Equal(expected, result.Tier);
    }

    [Fact]
    public void AssignAuto_DisablesGpuWhenEvenSmallestTierDoesNotFit()
    {
        var result = _assigner.AssignAuto(Gpu(2_000));
        Assert.True(result.Disabled);
        Assert.Null(result.Tier);
    }

    [Fact]
    public void AssignAuto_NeverPoolsAcrossGpus_DecisionIsPerCard()
    {
        // A 12GB card must not be auto-assigned "High" (24GB) just because a sibling GPU exists;
        // AssignAuto takes a single GpuDevice and has no way to see other cards at all, which is
        // itself the guarantee against VRAM pooling (docs/KERYX_RESEARCH.md §3).
        var result = _assigner.AssignAuto(Gpu(12_600));
        Assert.Equal(ModelTier.Default, result.Tier);
    }

    [Fact]
    public void AssignAuto_AccountsForAlreadyUsedVram()
    {
        // 12GB card but 5GB already used by something else -> only ~7GB free, below Default's
        // 12000MB requirement, so it should fall back to Light (8000MB - still won't fit) or
        // very-light.
        var result = _assigner.AssignAuto(Gpu(totalMb: 12_600, usedMb: 5_000));
        Assert.NotEqual(ModelTier.Default, result.Tier);
    }

    [Fact]
    public void AssignManual_FlagsUnderMinimumVram()
    {
        var result = _assigner.AssignManual(Gpu(6_000), ModelTier.VeryHigh);
        Assert.True(result.ForcedBelowRecommendedVram);
        Assert.Equal(ModelTier.VeryHigh, result.Tier);
    }

    [Fact]
    public void AssignManual_DoesNotFlagWhenVramIsSufficient()
    {
        var result = _assigner.AssignManual(Gpu(24_600), ModelTier.High);
        Assert.False(result.ForcedBelowRecommendedVram);
    }
}
