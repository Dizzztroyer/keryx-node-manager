using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.Localization;
using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.Safety;

public enum SafetyLevel
{
    Normal,
    Warning,
    Critical,
}

/// <summary>One GPU crossing a temperature threshold (or recovering back below one).</summary>
public sealed record SafetyEvent(GpuDevice Device, SafetyLevel Level, string Message);

/// <summary>
/// Brief §14 overheat protection: polls real GPU temperatures (via the same IGpuInfoProvider the
/// GPU page already uses, so this can never disagree with what the user sees there) while mining
/// is running, and raises an edge-triggered event when a card crosses into Warning/Critical or
/// recovers back to Normal. Deliberately does not call ProcessSupervisor/stop the miner itself -
/// that decision belongs to whoever owns the mining session (DashboardViewModel), which decides
/// what "Critical" actually does (stop-all) so this class stays a pure "tell me what's happening"
/// component, testable without a real process supervisor.
///
/// Edge-triggered, not level-triggered: a GPU sitting at a sustained 90°C should not raise a new
/// event on every single poll (that would spam the Dashboard/notifications every
/// MonitoringIntervalSeconds) - an event only fires when the *level itself changes* (Normal to
/// Warning, Warning to Critical, Critical back to Warning/Normal, etc.), which
/// ShouldRaiseEvent captures as pure, unit-testable logic separate from the polling loop.
/// </summary>
public sealed class SafetyMonitor
{
    private readonly IGpuInfoProvider _gpuInfoProvider;
    private readonly Dictionary<string, SafetyLevel> _lastLevel = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<SafetyEvent>? EventRaised;

    public bool IsRunning => _cts is not null;

    /// <summary>Last-known level for one GPU, or null if this monitor has never polled it (not
    /// running, or the GPU wasn't present in the most recent successful poll). Added so the new
    /// GPU-overclock feature can refuse to apply a clock/fan change while a card is already at
    /// Warning/Critical - see PROJECT_STATUS.md's fan/power-limit scoping note ("SafetyMonitor
    /// should own a known-safe floor/ceiling and refuse to relay a change that would defeat its own
    /// protection"). Deliberately read-only and non-invasive: this does not turn SafetyMonitor into
    /// an enforcement component itself, it just exposes the same state the polling loop already
    /// tracks internally so a caller (the overclock apply path) can make an informed decision.</summary>
    public SafetyLevel? GetLastLevel(string gpuUuid) =>
        _lastLevel.TryGetValue(gpuUuid, out var level) ? level : null;

    public SafetyMonitor(IGpuInfoProvider gpuInfoProvider)
    {
        _gpuInfoProvider = gpuInfoProvider;
    }

    /// <summary>
    /// Pure threshold decision, no I/O - unit-testable without a timer or real GPU. Critical is
    /// checked first since a temperature can validly satisfy both `>= warningC` and `>= criticalC`
    /// simultaneously (criticalC is expected to be the higher, more urgent threshold).
    /// </summary>
    public static SafetyLevel Evaluate(int temperatureC, int warningC, int criticalC)
    {
        if (temperatureC >= criticalC) return SafetyLevel.Critical;
        if (temperatureC >= warningC) return SafetyLevel.Warning;
        return SafetyLevel.Normal;
    }

    /// <summary>
    /// Whether a level transition is worth surfacing to the user - any actual change, in either
    /// direction (worsening or recovering). Separated from Evaluate so both halves of the
    /// edge-triggering logic are independently testable.
    /// </summary>
    public static bool ShouldRaiseEvent(SafetyLevel previous, SafetyLevel current) => current != previous;

    public static string BuildMessage(GpuDevice device, SafetyLevel level, int warningC, int criticalC) => level switch
    {
        SafetyLevel.Critical =>
            CoreStrings.Format("Safety.Critical", device.Name, device.TemperatureC, criticalC),
        SafetyLevel.Warning =>
            CoreStrings.Format("Safety.Warning", device.Name, device.TemperatureC, warningC),
        _ =>
            CoreStrings.Format("Safety.Normal", device.Name, device.TemperatureC),
    };

    public void Start(int pollIntervalSeconds, int warningC, int criticalC)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(pollIntervalSeconds, warningC, criticalC, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _lastLevel.Clear();
    }

    private async Task LoopAsync(int pollIntervalSeconds, int warningC, int criticalC, CancellationToken ct)
    {
        int delaySeconds = Math.Max(1, pollIntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var devices = await _gpuInfoProvider.QueryAsync(ct);
                foreach (var device in devices)
                {
                    // EventRaised is invoked synchronously, still inside this foreach - a
                    // subscriber reacting to a Critical event (DashboardViewModel) may call
                    // Stop() from within that same synchronous call, which cancels `ct` and
                    // clears _lastLevel. Without this check, the very next device in this same
                    // batch would see an empty _lastLevel and wrongly treat its already-known
                    // Warning state as a brand-new transition, firing a stale/confusing event
                    // after the monitor was supposedly already stopped (reproduced live: the
                    // Dashboard briefly showed a plain warning message overwriting the "stopped
                    // due to overheat" message for exactly this reason).
                    if (ct.IsCancellationRequested) break;

                    var level = Evaluate(device.TemperatureC, warningC, criticalC);
                    _lastLevel.TryGetValue(device.Uuid, out var previous);
                    if (ShouldRaiseEvent(previous, level))
                    {
                        _lastLevel[device.Uuid] = level;
                        EventRaised?.Invoke(new SafetyEvent(device, level, BuildMessage(device, level, warningC, criticalC)));
                    }
                }
            }
            catch (GpuQueryException)
            {
                // A query failure is "can't tell", not "safe" - but it's also not itself an
                // overheat event. The GPU page already surfaces query failures on its own; this
                // monitor just skips the poll and tries again next interval.
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
