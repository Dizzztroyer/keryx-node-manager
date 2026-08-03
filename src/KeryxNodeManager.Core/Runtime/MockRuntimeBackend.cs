using KeryxNodeManager.Core.Process;

namespace KeryxNodeManager.Core.Runtime;

/// <summary>
/// Simulates node/miner lifecycle (start, sync, mine, inference pause, crash, restart) with no
/// real process, for UI development/testing without Keryx binaries or GPUs installed (brief §23).
/// IMPORTANT: this backend must only ever be reachable via an explicit developer opt-in (a
/// --mock command-line switch or a DEBUG-only settings toggle) — never as an automatic fallback
/// when the real backends report unavailable, or a real user missing keryxd.exe would silently
/// get fake "everything is running" state instead of a clear setup error.
/// </summary>
public sealed class MockRuntimeBackend : IKeryxRuntimeBackend
{
    private readonly Random _random = new();

    public string Name => "mock";

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    private readonly Dictionary<ManagedProcessHandle, CancellationTokenSource> _fakeLogLoops = new();

    public Task<ManagedProcessHandle> StartNodeAsync(NodeLaunchSpec spec, CancellationToken ct = default)
    {
        var handle = new ManagedProcessHandle
        {
            Kind = ManagedProcessKind.Node,
            Pid = _random.Next(1000, 60000),
            StartedAt = DateTimeOffset.UtcNow,
            State = ManagedProcessState.Running,
        };
        StartFakeLogLoop(handle, spec.OnOutputLine, new[]
        {
            "Node synced to tip block",
            "GHOSTDAG: new blue block accepted",
            "Peer connected: 12 active",
        });
        return Task.FromResult(handle);
    }

    public Task<ManagedProcessHandle> StartMinerAsync(MinerLaunchSpec spec, CancellationToken ct = default)
    {
        var handle = new ManagedProcessHandle
        {
            Kind = ManagedProcessKind.Miner,
            Pid = _random.Next(1000, 60000),
            StartedAt = DateTimeOffset.UtcNow,
            State = ManagedProcessState.Running,
        };
        StartFakeLogLoop(handle, spec.OnOutputLine, new[]
        {
            "Inference pass complete, submitting share",
            "Hashrate: 12.4 blocks/hour (proof-of-model)",
            "Waiting for next mineable block template",
        });
        return Task.FromResult(handle);
    }

    /// <summary>Emits a rotating fake log line every few seconds so the Logs page (brief §12) has
    /// something real to display and export when run under --mock, without a real keryxd/keryx-miner
    /// process - mirrors the existing mock-jitter approach MockGpuInfoProvider already uses for the
    /// GPU page.</summary>
    private void StartFakeLogLoop(ManagedProcessHandle handle, Action<string, bool>? onOutputLine, string[] lines)
    {
        if (onOutputLine is null) return;
        var cts = new CancellationTokenSource();
        lock (_fakeLogLoops) _fakeLogLoops[handle] = cts;
        var ct = cts.Token;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            int i = 0;
            while (!ct.IsCancellationRequested)
            {
                try { await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(4), ct); }
                catch (OperationCanceledException) { break; }
                if (ct.IsCancellationRequested) break;
                onOutputLine(lines[i % lines.Length], false);
                i++;
            }
        }, CancellationToken.None);
    }

    public Task StopAsync(ManagedProcessHandle handle, TimeSpan gracePeriod, CancellationToken ct = default)
    {
        lock (_fakeLogLoops)
        {
            if (_fakeLogLoops.Remove(handle, out var cts)) cts.Cancel();
        }
        handle.State = ManagedProcessState.Stopped;
        handle.LastExitReason = "stopped by user (mock)";
        return Task.CompletedTask;
    }
}
