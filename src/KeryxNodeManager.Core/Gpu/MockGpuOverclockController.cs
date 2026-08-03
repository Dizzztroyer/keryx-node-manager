namespace KeryxNodeManager.Core.Gpu;

/// <summary>In-memory fake for --mock mode and unit tests - no real hardware, no NVAPI. Mirrors
/// MockGpuInfoProvider's role for read-only telemetry. Enforces the same capability range checks a
/// real implementation would (see ApplyClockOffsetsAsync/ApplyFanSpeedAsync), so tests against this
/// mock actually exercise the validation contract every real caller depends on, not just a
/// do-nothing stub.</summary>
public sealed class MockGpuOverclockController : IGpuOverclockController
{
    private readonly Dictionary<string, GpuOverclockState> _state = new();

    // Plausible, but explicitly fake, consumer-GPU range - a real implementation queries the
    // actual card via NVAPI instead of using these numbers for anything real.
    private static readonly GpuOverclockCapabilities MockCapabilities =
        new(MinCoreClockOffsetMhz: -200, MaxCoreClockOffsetMhz: 200,
            MinMemoryClockOffsetMhz: -500, MaxMemoryClockOffsetMhz: 1000,
            SupportsFanControl: true, MinFanPercent: 30, MaxFanPercent: 100);

    public Task<GpuOverclockCapabilities> GetCapabilitiesAsync(string gpuUuid, CancellationToken ct = default) =>
        Task.FromResult(MockCapabilities);

    public Task<GpuOverclockState> GetCurrentStateAsync(string gpuUuid, CancellationToken ct = default)
    {
        _state.TryGetValue(gpuUuid, out var state);
        return Task.FromResult(state ?? new GpuOverclockState(0, 0, null, FanIsAutoControlled: true));
    }

    public Task ApplyClockOffsetsAsync(string gpuUuid, int coreClockOffsetMhz, int memoryClockOffsetMhz, CancellationToken ct = default)
    {
        var caps = MockCapabilities;
        if (coreClockOffsetMhz < caps.MinCoreClockOffsetMhz || coreClockOffsetMhz > caps.MaxCoreClockOffsetMhz)
        {
            throw new GpuOverclockException(
                $"Core clock offset {coreClockOffsetMhz} MHz is outside this GPU's safe range " +
                $"({caps.MinCoreClockOffsetMhz}..{caps.MaxCoreClockOffsetMhz} MHz).");
        }
        if (memoryClockOffsetMhz < caps.MinMemoryClockOffsetMhz || memoryClockOffsetMhz > caps.MaxMemoryClockOffsetMhz)
        {
            throw new GpuOverclockException(
                $"Memory clock offset {memoryClockOffsetMhz} MHz is outside this GPU's safe range " +
                $"({caps.MinMemoryClockOffsetMhz}..{caps.MaxMemoryClockOffsetMhz} MHz).");
        }

        var existing = _state.TryGetValue(gpuUuid, out var s) ? s : new GpuOverclockState(0, 0, null, true);
        _state[gpuUuid] = existing with { CoreClockOffsetMhz = coreClockOffsetMhz, MemoryClockOffsetMhz = memoryClockOffsetMhz };
        return Task.CompletedTask;
    }

    public Task ApplyFanSpeedAsync(string gpuUuid, int? fanSpeedPercent, CancellationToken ct = default)
    {
        var caps = MockCapabilities;
        if (fanSpeedPercent is int percent && (percent < caps.MinFanPercent || percent > caps.MaxFanPercent))
        {
            throw new GpuOverclockException(
                $"Fan speed {percent}% is outside this GPU's safe range ({caps.MinFanPercent}..{caps.MaxFanPercent}%).");
        }

        var existing = _state.TryGetValue(gpuUuid, out var s) ? s : new GpuOverclockState(0, 0, null, true);
        _state[gpuUuid] = existing with { FanSpeedPercent = fanSpeedPercent, FanIsAutoControlled = fanSpeedPercent is null };
        return Task.CompletedTask;
    }

    public Task ResetToDefaultsAsync(string gpuUuid, CancellationToken ct = default)
    {
        _state[gpuUuid] = new GpuOverclockState(0, 0, null, FanIsAutoControlled: true);
        return Task.CompletedTask;
    }
}
