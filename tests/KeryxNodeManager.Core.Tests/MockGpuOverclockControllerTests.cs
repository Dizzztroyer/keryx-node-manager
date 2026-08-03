using KeryxNodeManager.Core.Gpu;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers MockGpuOverclockController's validation contract - the same range-checking behavior any
/// real IGpuOverclockController implementation (NvApiGpuOverclockController, only ever compiled
/// into KeryxNodeManager.App since it needs the Windows-only NvAPIWrapper package) is expected to
/// honor, so the GPU page's ViewModel-level logic (confirmation dialog, error surfacing) can be
/// fully tested against this mock without real hardware or a Windows-only dependency in this
/// cross-platform test project.
/// </summary>
public class MockGpuOverclockControllerTests
{
    [Fact]
    public async Task GetCurrentStateAsync_NeverTouched_ReturnsStockZeroOffsetsAndAutoFan()
    {
        var controller = new MockGpuOverclockController();

        var state = await controller.GetCurrentStateAsync("gpu-1");

        Assert.Equal(0, state.CoreClockOffsetMhz);
        Assert.Equal(0, state.MemoryClockOffsetMhz);
        Assert.Null(state.FanSpeedPercent);
        Assert.True(state.FanIsAutoControlled);
    }

    [Fact]
    public async Task ApplyClockOffsetsAsync_WithinRange_PersistsAndIsReadableAfterward()
    {
        var controller = new MockGpuOverclockController();

        await controller.ApplyClockOffsetsAsync("gpu-1", coreClockOffsetMhz: 100, memoryClockOffsetMhz: 500);
        var state = await controller.GetCurrentStateAsync("gpu-1");

        Assert.Equal(100, state.CoreClockOffsetMhz);
        Assert.Equal(500, state.MemoryClockOffsetMhz);
    }

    [Fact]
    public async Task ApplyClockOffsetsAsync_CoreOffsetOutsideCapabilities_ThrowsAndDoesNotPersist()
    {
        var controller = new MockGpuOverclockController();
        var caps = await controller.GetCapabilitiesAsync("gpu-1");

        await Assert.ThrowsAsync<GpuOverclockException>(() =>
            controller.ApplyClockOffsetsAsync("gpu-1", caps.MaxCoreClockOffsetMhz + 1, 0));

        var state = await controller.GetCurrentStateAsync("gpu-1");
        Assert.Equal(0, state.CoreClockOffsetMhz); // rejected value must not have partially applied
    }

    [Fact]
    public async Task ApplyClockOffsetsAsync_MemoryOffsetOutsideCapabilities_ThrowsAndDoesNotPersist()
    {
        var controller = new MockGpuOverclockController();
        var caps = await controller.GetCapabilitiesAsync("gpu-1");

        await Assert.ThrowsAsync<GpuOverclockException>(() =>
            controller.ApplyClockOffsetsAsync("gpu-1", 0, caps.MinMemoryClockOffsetMhz - 1));

        var state = await controller.GetCurrentStateAsync("gpu-1");
        Assert.Equal(0, state.MemoryClockOffsetMhz);
    }

    [Fact]
    public async Task ApplyFanSpeedAsync_WithinRange_PersistsAndClearsAutoFlag()
    {
        var controller = new MockGpuOverclockController();

        await controller.ApplyFanSpeedAsync("gpu-1", 75);
        var state = await controller.GetCurrentStateAsync("gpu-1");

        Assert.Equal(75, state.FanSpeedPercent);
        Assert.False(state.FanIsAutoControlled);
    }

    [Fact]
    public async Task ApplyFanSpeedAsync_OutsideRange_Throws()
    {
        var controller = new MockGpuOverclockController();
        var caps = await controller.GetCapabilitiesAsync("gpu-1");

        await Assert.ThrowsAsync<GpuOverclockException>(() =>
            controller.ApplyFanSpeedAsync("gpu-1", caps.MaxFanPercent + 1));
    }

    [Fact]
    public async Task ApplyFanSpeedAsync_NullSetsBackToAutoControlled()
    {
        var controller = new MockGpuOverclockController();
        await controller.ApplyFanSpeedAsync("gpu-1", 60);

        await controller.ApplyFanSpeedAsync("gpu-1", null);
        var state = await controller.GetCurrentStateAsync("gpu-1");

        Assert.Null(state.FanSpeedPercent);
        Assert.True(state.FanIsAutoControlled);
    }

    [Fact]
    public async Task ResetToDefaultsAsync_AfterChanges_RestoresStockZeroAndAutoFan()
    {
        var controller = new MockGpuOverclockController();
        await controller.ApplyClockOffsetsAsync("gpu-1", 100, 500);
        await controller.ApplyFanSpeedAsync("gpu-1", 80);

        await controller.ResetToDefaultsAsync("gpu-1");
        var state = await controller.GetCurrentStateAsync("gpu-1");

        Assert.Equal(0, state.CoreClockOffsetMhz);
        Assert.Equal(0, state.MemoryClockOffsetMhz);
        Assert.Null(state.FanSpeedPercent);
        Assert.True(state.FanIsAutoControlled);
    }

    [Fact]
    public async Task TwoDifferentGpuUuids_HaveIndependentState()
    {
        var controller = new MockGpuOverclockController();
        await controller.ApplyClockOffsetsAsync("gpu-1", 50, 0);

        var state1 = await controller.GetCurrentStateAsync("gpu-1");
        var state2 = await controller.GetCurrentStateAsync("gpu-2");

        Assert.Equal(50, state1.CoreClockOffsetMhz);
        Assert.Equal(0, state2.CoreClockOffsetMhz); // untouched GPU must not see gpu-1's change
    }
}
