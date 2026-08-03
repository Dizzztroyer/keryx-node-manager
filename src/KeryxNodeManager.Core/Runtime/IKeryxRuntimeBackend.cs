using KeryxNodeManager.Core.Process;

namespace KeryxNodeManager.Core.Runtime;

/// <summary>
/// Abstracts *how* keryxd/keryx-miner get launched so the UI and ProcessSupervisor never need to
/// know whether a process is a plain Windows child process or wrapped through wsl.exe. See
/// docs/ARCHITECTURE.md "Runtime backend abstraction" — NativeWindowsRuntimeBackend is the
/// default, WslRuntimeBackend is optional/secondary, Docker is not implemented in this pass.
/// </summary>
public interface IKeryxRuntimeBackend
{
    string Name { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<ManagedProcessHandle> StartNodeAsync(NodeLaunchSpec spec, CancellationToken ct = default);
    Task<ManagedProcessHandle> StartMinerAsync(MinerLaunchSpec spec, CancellationToken ct = default);
    Task StopAsync(ManagedProcessHandle handle, TimeSpan gracePeriod, CancellationToken ct = default);
}
