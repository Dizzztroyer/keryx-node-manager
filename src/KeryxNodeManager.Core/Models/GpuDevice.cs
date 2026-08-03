namespace KeryxNodeManager.Core.Models;

/// <summary>
/// A GPU as reported by nvidia-smi at a point in time. UUID is the stable identity used in
/// persisted profiles; CudaIndex is only valid for the current process launch (driver order can
/// change across reboots/driver updates — see docs/ARCHITECTURE.md "GPU identity").
/// </summary>
public sealed record GpuDevice
{
    public required string Uuid { get; init; }
    public required int CudaIndex { get; init; }
    public required string Name { get; init; }
    public required long TotalVramMb { get; init; }
    public long UsedVramMb { get; init; }
    public int UtilizationPercent { get; init; }
    public int TemperatureC { get; init; }
    public double PowerDrawW { get; init; }
    public double PowerLimitW { get; init; }
    public int CoreClockMhz { get; init; }
    public int MemoryClockMhz { get; init; }
    public int? FanSpeedPercent { get; init; }
    public string DriverVersion { get; init; } = string.Empty;
    public string? ComputeCapability { get; init; }
}

/// <summary>Per-GPU assignment stored in a MiningProfile. Keyed by GPU UUID, not index.</summary>
public sealed record GpuAssignment
{
    public required string GpuUuid { get; init; }
    /// <summary>"auto", "disabled", or a ModelTier name.</summary>
    public required string Mode { get; init; } = GpuAssignmentMode.Auto;
}
