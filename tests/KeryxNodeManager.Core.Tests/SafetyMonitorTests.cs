using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.Safety;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>Returns a fixed device list every call - just enough to drive SafetyMonitor's real
/// polling loop in a test without touching nvidia-smi.</summary>
internal sealed class FakeGpuInfoProvider : IGpuInfoProvider
{
    public IReadOnlyList<GpuDevice> Devices { get; set; } = Array.Empty<GpuDevice>();

    public Task<IReadOnlyList<GpuDevice>> QueryAsync(CancellationToken ct = default) =>
        Task.FromResult(Devices);
}

/// <summary>
/// Covers SafetyMonitor's pure decision logic (Evaluate/ShouldRaiseEvent/BuildMessage) - the
/// actual polling loop needs a real timer and is verified live instead (see PROJECT_STATUS.md),
/// matching this project's established pattern of splitting pure logic from I/O for testability.
/// </summary>
public class SafetyMonitorTests
{
    [Theory]
    [InlineData(50, 85, 95, SafetyLevel.Normal)]
    [InlineData(84, 85, 95, SafetyLevel.Normal)]
    [InlineData(85, 85, 95, SafetyLevel.Warning)]
    [InlineData(90, 85, 95, SafetyLevel.Warning)]
    [InlineData(94, 85, 95, SafetyLevel.Critical - 1)] // sanity guard against enum reordering, see below
    public void Evaluate_BoundaryTemperatures(int tempC, int warningC, int criticalC, SafetyLevel expected)
    {
        // The InlineData above for 94°C intentionally computes "Critical - 1" (i.e. Warning) as a
        // belt-and-suspenders check that the enum ordinal values haven't silently been reordered -
        // if someone later inserts a level between Warning and Critical, this test starts failing
        // loudly instead of the boundary test suite silently passing on stale assumptions.
        Assert.Equal(expected, SafetyMonitor.Evaluate(tempC, warningC, criticalC));
    }

    [Fact]
    public void Evaluate_AtOrAboveCriticalThreshold_ReturnsCritical()
    {
        Assert.Equal(SafetyLevel.Critical, SafetyMonitor.Evaluate(95, 85, 95));
        Assert.Equal(SafetyLevel.Critical, SafetyMonitor.Evaluate(110, 85, 95));
    }

    [Fact]
    public void Evaluate_BelowWarningThreshold_ReturnsNormal()
    {
        Assert.Equal(SafetyLevel.Normal, SafetyMonitor.Evaluate(30, 85, 95));
    }

    [Theory]
    [InlineData(SafetyLevel.Normal, SafetyLevel.Normal, false)]
    [InlineData(SafetyLevel.Normal, SafetyLevel.Warning, true)]
    [InlineData(SafetyLevel.Warning, SafetyLevel.Critical, true)]
    [InlineData(SafetyLevel.Critical, SafetyLevel.Warning, true)]
    [InlineData(SafetyLevel.Warning, SafetyLevel.Normal, true)]
    [InlineData(SafetyLevel.Critical, SafetyLevel.Critical, false)]
    public void ShouldRaiseEvent_OnlyFiresOnActualLevelChange(SafetyLevel previous, SafetyLevel current, bool expected)
    {
        Assert.Equal(expected, SafetyMonitor.ShouldRaiseEvent(previous, current));
    }

    [Fact]
    public void BuildMessage_CriticalMessage_MentionsStoppingMiningAndBothTemps()
    {
        var device = new GpuDevice { Uuid = "u", CudaIndex = 0, Name = "RTX 3090", TotalVramMb = 24000, TemperatureC = 97 };

        var message = SafetyMonitor.BuildMessage(device, SafetyLevel.Critical, warningC: 85, criticalC: 95);

        Assert.Contains("RTX 3090", message);
        Assert.Contains("97", message);
        Assert.Contains("95", message);
    }

    [Fact]
    public void BuildMessage_WarningMessage_DoesNotClaimMiningWasStopped()
    {
        var device = new GpuDevice { Uuid = "u", CudaIndex = 0, Name = "RTX 3090", TotalVramMb = 24000, TemperatureC = 88 };

        var message = SafetyMonitor.BuildMessage(device, SafetyLevel.Warning, warningC: 85, criticalC: 95);

        Assert.DoesNotContain("остановлен", message);
    }

    /// <summary>
    /// Regression test for a real race found during live verification: a subscriber reacting to a
    /// Critical event by calling Stop() synchronously (as DashboardViewModel does) must not cause
    /// a *different* GPU later in the same polling batch to fire a stale/confusing event, because
    /// Stop() clears the level-tracking dictionary out from under the in-progress loop. Two GPUs
    /// are queried: the first goes Critical (triggering Stop() from inside the handler), the
    /// second is already sitting in Warning and must NOT re-fire just because its remembered state
    /// was wiped.
    /// </summary>
    [Fact]
    public async Task Stop_CalledFromWithinEventHandler_DoesNotCauseStaleEventForLaterDeviceInSameBatch()
    {
        var hot = new GpuDevice { Uuid = "hot", CudaIndex = 0, Name = "Hot GPU", TotalVramMb = 24000, TemperatureC = 96 };
        var warm = new GpuDevice { Uuid = "warm", CudaIndex = 1, Name = "Warm GPU", TotalVramMb = 12000, TemperatureC = 88 };
        var provider = new FakeGpuInfoProvider { Devices = new[] { hot, warm } };
        var monitor = new SafetyMonitor(provider);

        var events = new List<SafetyEvent>();
        var criticalSeen = new TaskCompletionSource();
        monitor.EventRaised += evt =>
        {
            events.Add(evt);
            if (evt.Level == SafetyLevel.Critical)
            {
                monitor.Stop();
                criticalSeen.TrySetResult();
            }
        };

        monitor.Start(pollIntervalSeconds: 60, warningC: 85, criticalC: 95);
        var completed = await Task.WhenAny(criticalSeen.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(criticalSeen.Task, completed);

        // Give the loop a moment to (incorrectly, if the bug were present) process the second
        // device before honoring cancellation.
        await Task.Delay(200);

        Assert.Single(events);
        Assert.Equal(SafetyLevel.Critical, events[0].Level);
        Assert.Equal("hot", events[0].Device.Uuid);
    }

    [Fact]
    public void GetLastLevel_NeverPolled_ReturnsNull()
    {
        var monitor = new SafetyMonitor(new FakeGpuInfoProvider());

        Assert.Null(monitor.GetLastLevel("never-seen-uuid"));
    }

    /// <summary>Added alongside the new GPU-overclock feature (task #108-111): the overclock
    /// apply-path needs to check "is this GPU currently at Warning/Critical" before allowing a
    /// clock/fan change - this is the read-only accessor it relies on, so this test pins that a
    /// real poll actually populates it correctly, not just that Start()/Stop() don't throw.</summary>
    [Fact]
    public async Task GetLastLevel_AfterARealPoll_ReflectsTheLastObservedLevelForThatGpu()
    {
        var device = new GpuDevice { Uuid = "gpu-under-test", CudaIndex = 0, Name = "Test GPU", TotalVramMb = 8000, TemperatureC = 90 };
        var provider = new FakeGpuInfoProvider { Devices = new[] { device } };
        var monitor = new SafetyMonitor(provider);

        var warningSeen = new TaskCompletionSource();
        monitor.EventRaised += evt =>
        {
            if (evt.Level == SafetyLevel.Warning) warningSeen.TrySetResult();
        };

        monitor.Start(pollIntervalSeconds: 60, warningC: 85, criticalC: 95);
        var completed = await Task.WhenAny(warningSeen.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(warningSeen.Task, completed);

        Assert.Equal(SafetyLevel.Warning, monitor.GetLastLevel("gpu-under-test"));
        Assert.Null(monitor.GetLastLevel("some-other-gpu-not-in-this-batch"));

        monitor.Stop();
    }
}
