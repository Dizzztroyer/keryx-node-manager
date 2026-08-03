namespace KeryxNodeManager.Core.Process;

/// <summary>
/// Exponential backoff with a ceiling and an attempt cap, plus a "stable uptime resets the
/// counter" rule so a process that crashes once after running fine for hours doesn't inherit a
/// long backoff from an unrelated earlier incident.
/// </summary>
public sealed class RestartPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly TimeSpan _stableUptimeResetThreshold;

    private int _attempt;
    private DateTimeOffset? _lastStartedAt;

    public RestartPolicy(
        int maxAttempts = 5,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        TimeSpan? stableUptimeResetThreshold = null)
    {
        if (maxAttempts < 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        _maxAttempts = maxAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(5);
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
        _stableUptimeResetThreshold = stableUptimeResetThreshold ?? TimeSpan.FromMinutes(10);
    }

    public int AttemptCount => _attempt;

    public void NotifyStarted(DateTimeOffset now) => _lastStartedAt = now;

    /// <summary>Call when a managed process exits unexpectedly. Returns whether a restart should
    /// be attempted and, if so, the delay to wait before doing it.</summary>
    public (bool ShouldRestart, TimeSpan Delay) NotifyExited(DateTimeOffset now)
    {
        if (_lastStartedAt is { } startedAt && now - startedAt >= _stableUptimeResetThreshold)
        {
            _attempt = 0;
        }

        if (_attempt >= _maxAttempts)
        {
            return (false, TimeSpan.Zero);
        }

        var delay = ComputeDelay(_attempt);
        _attempt++;
        return (true, delay);
    }

    public void Reset() => _attempt = 0;

    private TimeSpan ComputeDelay(int attempt)
    {
        var seconds = _baseDelay.TotalSeconds * Math.Pow(2, attempt);
        var capped = Math.Min(seconds, _maxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(capped);
    }
}
