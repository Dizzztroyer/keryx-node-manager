namespace KeryxNodeManager.Core.Runtime;

/// <summary>
/// OnOutputLine (optional): called once per line of the child process's stdout/stderr, with
/// isError=true for stderr. This closes a real gap that predates the Logs page - both
/// NativeWindowsRuntimeBackend and WslRuntimeBackend already set RedirectStandardOutput/Error=true
/// on ProcessStartInfo, but until this callback was wired in, nothing ever read those redirected
/// streams. An unread redirected pipe fills its OS buffer once the child writes enough output and
/// then blocks the child on its next write - a real, if slow-to-trigger, hang bug for a
/// long-running node/miner process, not just a missing feature. Passing null is still safe (the
/// backends drain the streams into the callback machinery either way; a null callback just means
/// the lines are read and discarded rather than forwarded anywhere).
/// </summary>
public sealed record NodeLaunchSpec(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    Action<string, bool>? OnOutputLine = null);

public sealed record MinerLaunchSpec(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    Action<string, bool>? OnOutputLine = null);
