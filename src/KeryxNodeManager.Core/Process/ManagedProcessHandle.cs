namespace KeryxNodeManager.Core.Process;

public enum ManagedProcessKind { Node, Miner }

public enum ManagedProcessState { Stopped, Starting, Running, Restarting, Failed }

/// <summary>Lightweight handle the supervisor tracks; deliberately not the raw System.Diagnostics.Process
/// so callers (ViewModels) can't accidentally call OS-specific members outside the supervisor.</summary>
public sealed class ManagedProcessHandle
{
    public required ManagedProcessKind Kind { get; init; }
    public int? Pid { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public ManagedProcessState State { get; set; } = ManagedProcessState.Stopped;
    public string? LastExitReason { get; set; }
    public int? LastExitCode { get; set; }
}
