using KeryxNodeManager.Core.Gpu;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

public class NvidiaSmiGpuInfoProviderTests
{
    // Captured shape of `nvidia-smi --query-gpu=index,uuid,name,memory.total,memory.used,
    // utilization.gpu,temperature.gpu,power.draw,power.limit,clocks.sm,clocks.mem,fan.speed,
    // driver_version,compute_cap --format=csv,noheader,nounits` output.
    private const string SampleCsv =
        "0, GPU-11111111-1111-1111-1111-111111111111, NVIDIA GeForce RTX 3090, 24576, 1200, 5, 52, 45.30, 350.00, 1400, 9750, 40, 552.44, 8.6\n" +
        "1, GPU-22222222-2222-2222-2222-222222222222, NVIDIA GeForce RTX 5070, 12288, 400, 0, 38, 20.10, 220.00, 1200, 10500, [N/A], 552.44, 12.0\n";

    [Fact]
    public void ParseCsv_ParsesAllRows()
    {
        var devices = NvidiaSmiGpuInfoProvider.ParseCsv(SampleCsv);
        Assert.Equal(2, devices.Count);
    }

    [Fact]
    public void ParseCsv_MapsFieldsInDeclaredOrder()
    {
        var devices = NvidiaSmiGpuInfoProvider.ParseCsv(SampleCsv);
        var gpu0 = devices[0];
        Assert.Equal(0, gpu0.CudaIndex);
        Assert.Equal("GPU-11111111-1111-1111-1111-111111111111", gpu0.Uuid);
        Assert.Equal("NVIDIA GeForce RTX 3090", gpu0.Name);
        Assert.Equal(24576, gpu0.TotalVramMb);
        Assert.Equal(1200, gpu0.UsedVramMb);
        Assert.Equal(52, gpu0.TemperatureC);
        Assert.Equal(350.00, gpu0.PowerLimitW);
        Assert.Equal(40, gpu0.FanSpeedPercent);
        Assert.Equal("8.6", gpu0.ComputeCapability);
    }

    [Fact]
    public void ParseCsv_HandlesNonNumericFanSpeedGracefully()
    {
        var devices = NvidiaSmiGpuInfoProvider.ParseCsv(SampleCsv);
        var gpu1 = devices[1];
        Assert.Null(gpu1.FanSpeedPercent); // "[N/A]" - common on headless/server GPUs
    }

    [Fact]
    public void ParseCsv_ReturnsEmptyForBlankInput()
    {
        var devices = NvidiaSmiGpuInfoProvider.ParseCsv("");
        Assert.Empty(devices);
    }

    [Fact]
    public void ParseCsv_SkipsMalformedRowsInsteadOfThrowing()
    {
        var csv = SampleCsv + "this is not a valid csv row\n";
        var devices = NvidiaSmiGpuInfoProvider.ParseCsv(csv);
        Assert.Equal(2, devices.Count); // malformed trailing row silently skipped
    }
}
