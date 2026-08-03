using KeryxNodeManager.Core.Diagnostics;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.Models;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers the deterministic parts of SystemChecker (the wizard's brief §4 step 1). The process-
/// spawning checks (CheckWslAsync) are exercised indirectly on the real Windows machine during
/// manual wizard verification (see PROJECT_STATUS.md) rather than here, since this test project
/// runs cross-platform and wsl.exe's actual presence/absence is host-dependent — what IS testable
/// everywhere is that CheckNvidiaAsync correctly reflects whatever IGpuInfoProvider reports,
/// without re-implementing nvidia-smi parsing (already covered by NvidiaSmiGpuInfoProviderTests).
/// </summary>
public class SystemCheckerTests
{
    private sealed class FakeGpuInfoProvider : IGpuInfoProvider
    {
        private readonly IReadOnlyList<GpuDevice> _devices;
        private readonly GpuQueryException? _throwOnQuery;

        public FakeGpuInfoProvider(IReadOnlyList<GpuDevice> devices) => _devices = devices;
        public FakeGpuInfoProvider(GpuQueryException ex)
        {
            _devices = Array.Empty<GpuDevice>();
            _throwOnQuery = ex;
        }

        public Task<IReadOnlyList<GpuDevice>> QueryAsync(CancellationToken ct = default)
        {
            if (_throwOnQuery is not null) throw _throwOnQuery;
            return Task.FromResult(_devices);
        }
    }

    [Fact]
    public void CheckWindowsVersion_NeverThrows_AndReturnsRequiredCheck()
    {
        var result = SystemChecker.CheckWindowsVersion();

        Assert.Equal("Версия Windows", result.Name);
        Assert.True(result.Required);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        // Whatever OS this test runs on, the check must produce a definite pass/fail, not throw.
    }

    [Fact]
    public void CheckDocker_NeverThrows_AndIsInformationalOnly()
    {
        var result = SystemChecker.CheckDocker();

        Assert.Equal("Docker (необязательно)", result.Name);
        Assert.False(result.Required); // must never block the wizard
    }

    [Fact]
    public async Task CheckNvidiaAsync_DevicesFound_PassesAndListsNames()
    {
        var devices = new List<GpuDevice>
        {
            new() { Uuid = "A", CudaIndex = 0, Name = "NVIDIA GeForce RTX 3060", TotalVramMb = 12_288 },
        };
        var provider = new FakeGpuInfoProvider(devices);

        var result = await SystemChecker.CheckNvidiaAsync(provider);

        Assert.True(result.Passed);
        Assert.True(result.Required);
        Assert.Contains("RTX 3060", result.Detail);
    }

    [Fact]
    public async Task CheckNvidiaAsync_NoDevicesReturned_Fails()
    {
        var provider = new FakeGpuInfoProvider(new List<GpuDevice>());

        var result = await SystemChecker.CheckNvidiaAsync(provider);

        Assert.False(result.Passed);
        Assert.True(result.Required);
    }

    [Fact]
    public async Task CheckNvidiaAsync_ProviderThrowsGpuQueryException_FailsWithMessage()
    {
        var provider = new FakeGpuInfoProvider(new GpuQueryException("nvidia-smi не найден."));

        var result = await SystemChecker.CheckNvidiaAsync(provider);

        Assert.False(result.Passed);
        Assert.Equal("nvidia-smi не найден.", result.Detail);
    }
}
