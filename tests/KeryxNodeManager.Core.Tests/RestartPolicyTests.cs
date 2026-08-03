using KeryxNodeManager.Core.Process;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

public class RestartPolicyTests
{
    [Fact]
    public void NotifyExited_UsesExponentialBackoff()
    {
        var policy = new RestartPolicy(maxAttempts: 5, baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromMinutes(10));
        var now = DateTimeOffset.UtcNow;

        var (r1, d1) = policy.NotifyExited(now);
        var (r2, d2) = policy.NotifyExited(now);
        var (r3, d3) = policy.NotifyExited(now);

        Assert.True(r1); Assert.True(r2); Assert.True(r3);
        Assert.Equal(TimeSpan.FromSeconds(1), d1);
        Assert.Equal(TimeSpan.FromSeconds(2), d2);
        Assert.Equal(TimeSpan.FromSeconds(4), d3);
    }

    [Fact]
    public void NotifyExited_StopsAfterMaxAttempts()
    {
        var policy = new RestartPolicy(maxAttempts: 2, baseDelay: TimeSpan.FromMilliseconds(1));
        var now = DateTimeOffset.UtcNow;

        Assert.True(policy.NotifyExited(now).ShouldRestart);
        Assert.True(policy.NotifyExited(now).ShouldRestart);
        Assert.False(policy.NotifyExited(now).ShouldRestart);
    }

    [Fact]
    public void NotifyExited_DelayIsCappedAtMaxDelay()
    {
        var policy = new RestartPolicy(maxAttempts: 20, baseDelay: TimeSpan.FromSeconds(10), maxDelay: TimeSpan.FromSeconds(30));
        var now = DateTimeOffset.UtcNow;

        TimeSpan last = TimeSpan.Zero;
        for (int i = 0; i < 6; i++)
        {
            last = policy.NotifyExited(now).Delay;
        }
        Assert.Equal(TimeSpan.FromSeconds(30), last);
    }

    [Fact]
    public void NotifyExited_ResetsAttemptCounterAfterStableUptime()
    {
        var policy = new RestartPolicy(
            maxAttempts: 1,
            baseDelay: TimeSpan.FromMilliseconds(1),
            stableUptimeResetThreshold: TimeSpan.FromMinutes(10));

        var t0 = DateTimeOffset.UtcNow;
        Assert.True(policy.NotifyExited(t0).ShouldRestart); // attempt 1, now at limit
        Assert.False(policy.NotifyExited(t0).ShouldRestart); // attempt 2 exceeds maxAttempts=1

        // Simulate the process having been restarted and then run stably for >10 minutes before
        // crashing again - counter should reset instead of staying exhausted forever.
        policy.NotifyStarted(t0);
        var muchLater = t0.AddMinutes(15);
        Assert.True(policy.NotifyExited(muchLater).ShouldRestart);
    }

    [Fact]
    public void Reset_ClearsAttemptCounter()
    {
        var policy = new RestartPolicy(maxAttempts: 1, baseDelay: TimeSpan.FromMilliseconds(1));
        var now = DateTimeOffset.UtcNow;
        policy.NotifyExited(now);
        policy.Reset();
        Assert.Equal(0, policy.AttemptCount);
    }
}
