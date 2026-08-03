using KeryxNodeManager.Core.Localization;
using KeryxNodeManager.Core.Runtime;

namespace KeryxNodeManager.Core.Process;

public sealed record SupervisorEvent(ManagedProcessKind Kind, string Message, DateTimeOffset At);

/// <summary>
/// Ties a RestartPolicy to an IKeryxRuntimeBackend for one managed process (node or miner).
/// Guarantees single-launch (won't start a second instance while one is Running/Starting),
/// applies backoff on unexpected exit, and stops trying after MaxAttempts (brief §3.5).
/// </summary>
public sealed class ProcessSupervisor : IDisposable
{
    private readonly IKeryxRuntimeBackend _backend;
    private readonly RestartPolicy _restartPolicy;
    private readonly ManagedProcessKind _kind;
    private readonly bool _autoRestart;
    private readonly List<SupervisorEvent> _history = new();
    private ManagedProcessHandle? _handle;
    private CancellationTokenSource? _watchCts;

    public event Action<SupervisorEvent>? EventRaised;

    public ProcessSupervisor(
        IKeryxRuntimeBackend backend,
        ManagedProcessKind kind,
        bool autoRestart,
        int maxRestartAttempts,
        int restartBaseDelaySeconds)
    {
        _backend = backend;
        _kind = kind;
        _autoRestart = autoRestart;
        _restartPolicy = new RestartPolicy(maxRestartAttempts, TimeSpan.FromSeconds(restartBaseDelaySeconds));
    }

    public ManagedProcessHandle? CurrentHandle => _handle;

    public bool IsRunning => _handle is { State: ManagedProcessState.Running or ManagedProcessState.Starting };

    public async Task StartNodeAsync(Runtime.NodeLaunchSpec spec, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            Raise(CoreStrings.Get("Process.AlreadyRunning"));
            return;
        }
        _handle = await _backend.StartNodeAsync(spec, ct);
        _restartPolicy.NotifyStarted(DateTimeOffset.UtcNow);
        Raise(CoreStrings.Format("Process.NodeStarted", _handle.Pid));
        WatchForCrash(() => _backend.StartNodeAsync(spec, CancellationToken.None));
    }

    public async Task StartMinerAsync(Runtime.MinerLaunchSpec spec, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            Raise(CoreStrings.Get("Process.AlreadyRunning"));
            return;
        }
        _handle = await _backend.StartMinerAsync(spec, ct);
        _restartPolicy.NotifyStarted(DateTimeOffset.UtcNow);
        Raise(CoreStrings.Format("Process.MinerStarted", _handle.Pid));
        WatchForCrash(() => _backend.StartMinerAsync(spec, CancellationToken.None));
    }

    public async Task StopAsync(TimeSpan gracePeriod, CancellationToken ct = default)
    {
        _watchCts?.Cancel();
        if (_handle is null) return;
        await _backend.StopAsync(_handle, gracePeriod, ct);
        _restartPolicy.Reset();
        Raise(CoreStrings.Get("Process.StoppedByUser"));
    }

    /// <summary>Polls the handle's state on a background loop and applies restart policy on an
    /// unexpected transition to Stopped/Failed. A real implementation driven by process.Exited
    /// events (as NativeWindowsRuntimeBackend already wires up) is preferred where available;
    /// this loop is the portable fallback used by the App layer's health-check timer.</summary>
    private void WatchForCrash(Func<Task<ManagedProcessHandle>> restart)
    {
        _watchCts?.Cancel();
        _watchCts = new CancellationTokenSource();
        var ct = _watchCts.Token;
        var handleAtStart = _handle;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2), ct).ContinueWith(_ => { });
                if (ct.IsCancellationRequested) break;
                if (handleAtStart is null) break;

                if (handleAtStart.State == ManagedProcessState.Stopped && _autoRestart)
                {
                    var (shouldRestart, delay) = _restartPolicy.NotifyExited(DateTimeOffset.UtcNow);
                    if (!shouldRestart)
                    {
                        Raise(CoreStrings.Get("Process.RestartLimitReached"));
                        break;
                    }
                    // :0 rounds to a whole number of seconds before it ever reaches the format
                    // string - CoreStrings.Format only substitutes the already-formatted string, it
                    // doesn't apply numeric format specifiers itself.
                    Raise(CoreStrings.Format("Process.RestartingSoon",
                        delay.TotalSeconds.ToString("0"), _restartPolicy.AttemptCount));
                    await System.Threading.Tasks.Task.Delay(delay, ct).ContinueWith(_ => { });
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        _handle = await restart();
                        handleAtStart = _handle;
                        _restartPolicy.NotifyStarted(DateTimeOffset.UtcNow);
                        Raise(CoreStrings.Format("Process.Restarted", _handle.Pid));
                    }
                    catch (Exception ex)
                    {
                        Raise(CoreStrings.Format("Process.RestartFailed", ex.Message));
                        break;
                    }
                }
                else if (handleAtStart.State == ManagedProcessState.Stopped)
                {
                    break;
                }
            }
        }, CancellationToken.None);
    }

    private void Raise(string message)
    {
        var evt = new SupervisorEvent(_kind, message, DateTimeOffset.UtcNow);
        _history.Add(evt);
        EventRaised?.Invoke(evt);
    }

    public IReadOnlyList<SupervisorEvent> History => _history;

    public void Dispose() => _watchCts?.Cancel();
}
