using KeryxNodeManager.Core.Gpu;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;

namespace KeryxNodeManager.App.Gpu;

/// <summary>
/// Real GPU overclock/fan-speed control via NVAPI (NvAPIWrapper.Net) - the same undocumented-but-
/// stable private API MSI Afterburner/HWiNFO/EVGA Precision use, since neither NVIDIA nor Windows
/// expose an officially documented API for consumer-GeForce clock offsets or fan control (see
/// IGpuOverclockController's doc comment and PROJECT_STATUS.md's original fan/power-limit scoping
/// note for the full risk reasoning). Only ever constructed/used from KeryxNodeManager.App -
/// Core stays cross-platform-buildable and never references this Windows-only package directly.
///
/// Actually applying an offset uses the LOW-LEVEL <c>NvAPIWrapper.Native.GPUApi.SetClockBoostTable</c>
/// entry point, not a convenient high-level setter - the high-level <see cref="PhysicalGPU.PerformanceStatesInfo"/>
/// façade this library exposes is read-only (confirmed by inspecting the installed package's public
/// API surface: GPUPerformanceStateClock.ClockDeltaInkHz has no setter). The classic NVAPI
/// ClockBoostTable is a fixed-size per-domain delta array using NVIDIA's own well-established slot
/// convention (matching the public nvapi.h NVAPI_GPU_PUBLIC_CLOCK_* constants used by essentially
/// every third-party NVAPI overclocking tool): index 0 = graphics/core clock, index 4 = memory
/// clock. This class preserves every other slot's existing value when writing (read-modify-write),
/// so applying a core/memory offset can never accidentally zero out or corrupt an unrelated domain
/// this app never intended to touch.
/// </summary>
public sealed class NvApiGpuOverclockController : IGpuOverclockController
{
    private const int GraphicsClockSlot = 0;
    private const int MemoryClockSlot = 4;

    private IReadOnlyList<Core.Models.GpuDevice> _knownDevices = Array.Empty<Core.Models.GpuDevice>();

    /// <summary>Must be called (with the same GpuDevice list the GPU page's IGpuInfoProvider just
    /// queried) before any other method on this class - see ResolveGpu's doc comment for why UUID
    /// matching goes through this list rather than any NVAPI-native identifier.</summary>
    public void SetKnownDevices(IReadOnlyList<Core.Models.GpuDevice> devices) => _knownDevices = devices;

    private static PhysicalGPU ResolveGpu(string gpuUuid, IReadOnlyList<Core.Models.GpuDevice> knownDevices)
    {
        var index = knownDevices.ToList().FindIndex(d => d.Uuid == gpuUuid);
        if (index < 0)
        {
            throw new GpuOverclockException($"GPU with UUID {gpuUuid} is not in the current device list.");
        }

        var gpus = PhysicalGPU.GetPhysicalGPUs();
        if (index >= gpus.Length)
        {
            throw new GpuOverclockException(
                $"NVAPI reports {gpus.Length} GPU(s), but device index {index} was expected - " +
                "GPU enumeration mismatch between nvidia-smi and NVAPI.");
        }
        return gpus[index];
    }

    public Task<GpuOverclockCapabilities> GetCapabilitiesAsync(string gpuUuid, CancellationToken ct = default)
    {
        var gpu = ResolveGpu(gpuUuid, _knownDevices);

        int coreMin = 0, coreMax = 0, memMin = 0, memMax = 0;
        try
        {
            var ranges = GPUApi.GetClockBoostRanges(gpu.Handle).ClockBoostRanges;
            if (ranges.Length > GraphicsClockSlot)
            {
                coreMin = ranges[GraphicsClockSlot].MinimumInkHz / 1000;
                coreMax = ranges[GraphicsClockSlot].MaximumInkHz / 1000;
            }
            if (ranges.Length > MemoryClockSlot)
            {
                memMin = ranges[MemoryClockSlot].MinimumInkHz / 1000;
                memMax = ranges[MemoryClockSlot].MaximumInkHz / 1000;
            }
        }
        catch (Exception ex)
        {
            throw new GpuOverclockException($"Failed to read clock-offset ranges from NVAPI: {ex.Message}", ex);
        }

        var coolers = gpu.CoolerInformation.Coolers.ToList();
        var hasFanControl = coolers.Count > 0;

        return Task.FromResult(new GpuOverclockCapabilities(
            MinCoreClockOffsetMhz: coreMin,
            MaxCoreClockOffsetMhz: coreMax,
            MinMemoryClockOffsetMhz: memMin,
            MaxMemoryClockOffsetMhz: memMax,
            SupportsFanControl: hasFanControl,
            MinFanPercent: hasFanControl ? coolers[0].CurrentMinimumLevel : 0,
            MaxFanPercent: hasFanControl ? coolers[0].CurrentMaximumLevel : 0));
    }

    public Task<GpuOverclockState> GetCurrentStateAsync(string gpuUuid, CancellationToken ct = default)
    {
        var gpu = ResolveGpu(gpuUuid, _knownDevices);

        int coreOffsetMhz = 0, memOffsetMhz = 0;
        try
        {
            var deltas = GPUApi.GetClockBoostTable(gpu.Handle).GPUDeltas;
            if (deltas.Length > GraphicsClockSlot) coreOffsetMhz = deltas[GraphicsClockSlot].FrequencyDeltaInkHz / 1000;
            if (deltas.Length > MemoryClockSlot) memOffsetMhz = deltas[MemoryClockSlot].FrequencyDeltaInkHz / 1000;
        }
        catch (Exception ex)
        {
            throw new GpuOverclockException($"Failed to read current clock offsets from NVAPI: {ex.Message}", ex);
        }

        var coolers = gpu.CoolerInformation.Coolers.ToList();
        int? fanPercent = coolers.Count > 0 ? coolers[0].CurrentLevel : null;
        bool fanAuto = coolers.Count == 0 || coolers[0].CurrentPolicy == CoolerPolicy.Performance;

        return Task.FromResult(new GpuOverclockState(coreOffsetMhz, memOffsetMhz, fanPercent, fanAuto));
    }

    public Task ApplyClockOffsetsAsync(string gpuUuid, int coreClockOffsetMhz, int memoryClockOffsetMhz, CancellationToken ct = default)
    {
        var gpu = ResolveGpu(gpuUuid, _knownDevices);
        try
        {
            // Read-modify-write: start from the CURRENT table so slots this app doesn't manage
            // (anything other than graphics/memory) are preserved exactly as-is.
            var current = GPUApi.GetClockBoostTable(gpu.Handle);
            var deltas = current.GPUDeltas.ToArray();

            if (deltas.Length > GraphicsClockSlot)
            {
                deltas[GraphicsClockSlot] = new PrivateClockBoostTableV1.GPUDelta(coreClockOffsetMhz * 1000);
            }
            if (deltas.Length > MemoryClockSlot)
            {
                deltas[MemoryClockSlot] = new PrivateClockBoostTableV1.GPUDelta(memoryClockOffsetMhz * 1000);
            }

            GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(deltas));
        }
        catch (Exception ex)
        {
            throw new GpuOverclockException(
                $"Failed to apply clock offsets (core {coreClockOffsetMhz} MHz, memory {memoryClockOffsetMhz} MHz) via NVAPI: {ex.Message}", ex);
        }
        return Task.CompletedTask;
    }

    public Task ApplyFanSpeedAsync(string gpuUuid, int? fanSpeedPercent, CancellationToken ct = default)
    {
        var gpu = ResolveGpu(gpuUuid, _knownDevices);
        var coolerInfo = gpu.CoolerInformation;
        var coolers = coolerInfo.Coolers.ToList();
        if (coolers.Count == 0)
        {
            throw new GpuOverclockException("This GPU has no NVAPI-controllable cooler (common on cards without exposed fan control).");
        }
        var coolerId = coolers[0].CoolerId;

        try
        {
            if (fanSpeedPercent is int percent)
            {
                coolerInfo.SetCoolerSettings(coolerId, percent);
            }
            else
            {
                coolerInfo.SetCoolerSettings(coolerId, CoolerPolicy.Performance);
            }
        }
        catch (Exception ex)
        {
            throw new GpuOverclockException($"Failed to set fan speed via NVAPI: {ex.Message}", ex);
        }
        return Task.CompletedTask;
    }

    public async Task ResetToDefaultsAsync(string gpuUuid, CancellationToken ct = default)
    {
        await ApplyClockOffsetsAsync(gpuUuid, 0, 0, ct);
        await ApplyFanSpeedAsync(gpuUuid, null, ct);
    }
}
