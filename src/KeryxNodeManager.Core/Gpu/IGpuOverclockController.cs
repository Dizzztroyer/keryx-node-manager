namespace KeryxNodeManager.Core.Gpu;

/// <summary>What one GPU actually reports it can safely accept, queried live rather than
/// hardcoded - the same driver/hardware/vBIOS combination determines real min/max offsets, and a
/// wrong guess here would either silently reject a value the card could handle or (far worse)
/// accept one it can't. <c>SupportsFanControl</c> matters because consumer GeForce cards typically
/// do NOT expose fan control (only workstation/datacenter Quadro/Tesla cards do via nvidia-smi;
/// GeForce needs NVAPI specifically, and even NVAPI doesn't guarantee every model exposes it) - see
/// PROJECT_STATUS.md's original fan/power-limit scoping note.</summary>
public sealed record GpuOverclockCapabilities(
    int MinCoreClockOffsetMhz,
    int MaxCoreClockOffsetMhz,
    int MinMemoryClockOffsetMhz,
    int MaxMemoryClockOffsetMhz,
    bool SupportsFanControl,
    int MinFanPercent,
    int MaxFanPercent);

/// <summary>Current applied state - what's actually active on the card right now, not what's
/// persisted in a profile (those can differ if the app crashed mid-apply, the driver reset on
/// reboot, or the user changed something with a different tool like MSI Afterburner).</summary>
public sealed record GpuOverclockState(
    int CoreClockOffsetMhz,
    int MemoryClockOffsetMhz,
    int? FanSpeedPercent,
    bool FanIsAutoControlled);

/// <summary>
/// Writes clock offsets and fan speed to real NVIDIA hardware - genuinely different risk than
/// <see cref="IGpuInfoProvider"/>'s read-only telemetry (see PROJECT_STATUS.md's fan/power-limit
/// scoping note for the full reasoning). Implemented for real only in KeryxNodeManager.App (via the
/// NvAPIWrapper NuGet package, a Windows-only native-interop dependency that cannot live in this
/// cross-platform-buildable Core project - see KeryxNodeManager.Core.csproj's own doc comment);
/// <see cref="MockGpuOverclockController"/> here is what --mock mode and every unit test use
/// instead, so Core's test suite never needs real hardware or NVAPI to run.
///
/// Every apply method takes the GPU's Uuid (matching <see cref="Models.GpuDevice.Uuid"/>), not an
/// index - indices can shift if a card is removed/reordered, UUIDs never do (this mirrors
/// GpuAssignment's own existing UUID-keyed persistence pattern).
/// </summary>
public interface IGpuOverclockController
{
    Task<GpuOverclockCapabilities> GetCapabilitiesAsync(string gpuUuid, CancellationToken ct = default);

    Task<GpuOverclockState> GetCurrentStateAsync(string gpuUuid, CancellationToken ct = default);

    /// <summary>Applies BOTH offsets together in one call rather than two separate ones -
    /// NVAPI's real performance-state API applies clock deltas as a set for a given state, so
    /// splitting this into two calls could leave a card in a half-applied, never-actually-valid
    /// intermediate configuration if the second call failed.</summary>
    Task ApplyClockOffsetsAsync(string gpuUuid, int coreClockOffsetMhz, int memoryClockOffsetMhz, CancellationToken ct = default);

    /// <summary>Fixed fan speed percent, not a full temperature-based curve - a full curve editor
    /// is real added UI/design complexity this pass didn't attempt; a single target percent (with
    /// "Auto" meaning null = hand control back to the driver) covers the user's actual request
    /// ("let those who want to, overclock/control their fans") without over-building.</summary>
    Task ApplyFanSpeedAsync(string gpuUuid, int? fanSpeedPercent, CancellationToken ct = default);

    /// <summary>Resets clocks to stock (0/0 offset) and fan back to automatic/driver control -
    /// the "get me back to safe" escape hatch every apply-capable feature in this project has had
    /// (mirrors BinaryUpdateService's own backup-before-overwrite safety net).</summary>
    Task ResetToDefaultsAsync(string gpuUuid, CancellationToken ct = default);
}

public sealed class GpuOverclockException(string message, Exception? inner = null) : Exception(message, inner);
