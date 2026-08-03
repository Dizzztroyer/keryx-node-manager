using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.Gpu;

/// <summary>
/// Simulates a 3-GPU mixed rig for UI development/testing without real hardware (brief §23).
/// Temperature/utilization drift a little on each call so the dashboard has something to show.
/// Never selected automatically in a Release build — see MockRuntimeBackend remarks.
/// </summary>
public sealed class MockGpuInfoProvider : IGpuInfoProvider
{
    private readonly Random _random = new(12345);
    private readonly List<GpuDevice> _devices;

    public MockGpuInfoProvider()
    {
        _devices = new List<GpuDevice>
        {
            new()
            {
                Uuid = "GPU-00000000-0000-0000-0000-000000000001",
                CudaIndex = 0,
                Name = "NVIDIA GeForce RTX 3060",
                TotalVramMb = 12_288,
                UsedVramMb = 512,
                UtilizationPercent = 0,
                TemperatureC = 45,
                PowerDrawW = 35,
                PowerLimitW = 170,
                CoreClockMhz = 1200,
                MemoryClockMhz = 7000,
                FanSpeedPercent = 30,
                DriverVersion = "552.44",
                ComputeCapability = "8.6",
            },
            new()
            {
                Uuid = "GPU-00000000-0000-0000-0000-000000000002",
                CudaIndex = 1,
                Name = "NVIDIA GeForce RTX 3090",
                TotalVramMb = 24_576,
                UsedVramMb = 1024,
                UtilizationPercent = 0,
                TemperatureC = 48,
                PowerDrawW = 40,
                PowerLimitW = 350,
                CoreClockMhz = 1300,
                MemoryClockMhz = 9750,
                FanSpeedPercent = 35,
                DriverVersion = "552.44",
                ComputeCapability = "8.6",
            },
            new()
            {
                Uuid = "GPU-00000000-0000-0000-0000-000000000003",
                CudaIndex = 2,
                Name = "NVIDIA GeForce RTX 5070",
                TotalVramMb = 12_288,
                UsedVramMb = 512,
                UtilizationPercent = 0,
                TemperatureC = 42,
                PowerDrawW = 30,
                PowerLimitW = 220,
                CoreClockMhz = 1500,
                MemoryClockMhz = 10500,
                FanSpeedPercent = 25,
                DriverVersion = "552.44",
                ComputeCapability = "12.0",
            },
        };
    }

    public Task<IReadOnlyList<GpuDevice>> QueryAsync(CancellationToken ct = default)
    {
        var jittered = _devices.Select(d => d with
        {
            TemperatureC = d.TemperatureC + _random.Next(-2, 3),
            UtilizationPercent = Math.Clamp(d.UtilizationPercent + _random.Next(-5, 6), 0, 100),
        }).ToList();
        return Task.FromResult<IReadOnlyList<GpuDevice>>(jittered);
    }
}
