using System.Diagnostics;
using KeryxNodeManager.Core.Localization;
using KeryxNodeManager.Core.Process;

namespace KeryxNodeManager.Core.Runtime;

/// <summary>
/// Default backend: runs keryxd.exe / keryx-miner.exe directly as native Windows processes.
/// docs/KERYX_RESEARCH.md §1 confirms both ship official win64 binaries, so no WSL/Docker layer
/// is needed for the common case. Every launch uses ProcessStartInfo.ArgumentList (never a
/// concatenated command string) and CreateNoWindow=true so no console flashes up
/// (brief §3.5 "no popping-up console windows").
/// </summary>
public sealed class NativeWindowsRuntimeBackend : IKeryxRuntimeBackend
{
    private readonly Dictionary<ManagedProcessHandle, System.Diagnostics.Process> _processes = new();

    public string Name => "native";

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(OperatingSystem.IsWindows());

    public Task<ManagedProcessHandle> StartNodeAsync(NodeLaunchSpec spec, CancellationToken ct = default) =>
        StartAsync(ManagedProcessKind.Node, spec.ExecutablePath, spec.Arguments, spec.WorkingDirectory,
            spec.EnvironmentVariables, spec.OnOutputLine);

    public Task<ManagedProcessHandle> StartMinerAsync(MinerLaunchSpec spec, CancellationToken ct = default) =>
        StartAsync(ManagedProcessKind.Miner, spec.ExecutablePath, spec.Arguments, spec.WorkingDirectory,
            spec.EnvironmentVariables, spec.OnOutputLine);

    private Task<ManagedProcessHandle> StartAsync(
        ManagedProcessKind kind,
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environmentVariables,
        Action<string, bool>? onOutputLine)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                CoreStrings.Format("Runtime.ExecutableNotFound", executablePath), executablePath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        foreach (var (key, value) in environmentVariables) psi.Environment[key] = value;

        var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        var handle = new ManagedProcessHandle { Kind = kind, State = ManagedProcessState.Starting };

        process.Exited += (_, _) =>
        {
            handle.State = ManagedProcessState.Stopped;
            handle.LastExitCode = SafeExitCode(process);
        };

        // Always drain the redirected pipes via the async event-based API, whether or not a
        // caller supplied onOutputLine - critical because keryxd/keryx-miner are long-running
        // processes that write continuously, and RedirectStandardOutput/Error=true above means the
        // OS pipe buffer WILL fill and block the child's next write if nobody reads it. Previously
        // nothing called BeginOutputReadLine/BeginErrorReadLine at all, so every launch had this
        // latent hang bug regardless of the Logs feature; forwarding to onOutputLine (if given) is
        // a separate concern from the always-required draining.
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine?.Invoke(e.Data, false); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine?.Invoke(e.Data, true); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        handle.Pid = process.Id;
        handle.StartedAt = DateTimeOffset.UtcNow;
        handle.State = ManagedProcessState.Running;

        lock (_processes) _processes[handle] = process;
        return Task.FromResult(handle);
    }

    public async Task StopAsync(ManagedProcessHandle handle, TimeSpan gracePeriod, CancellationToken ct = default)
    {
        System.Diagnostics.Process? process;
        lock (_processes)
        {
            if (!_processes.TryGetValue(handle, out process)) return;
        }

        if (process.HasExited)
        {
            handle.State = ManagedProcessState.Stopped;
            return;
        }

        // Console apps (keryxd.exe/keryx-miner.exe) don't have a main window to close
        // cooperatively; CloseMainWindow() is a no-op for them. We attempt it anyway (harmless)
        // then wait out the grace period before a hard kill of the whole process tree, so a
        // process that does trap SIGTERM-equivalent shutdown gets the chance.
        try { process.CloseMainWindow(); } catch { /* no main window - expected for console apps */ }

        try
        {
            await process.WaitForExitAsync(new CancellationTokenSource(gracePeriod).Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct);
            }
        }

        handle.State = ManagedProcessState.Stopped;
        handle.LastExitReason = "stopped by user";
        lock (_processes) _processes.Remove(handle);
    }

    private static int? SafeExitCode(System.Diagnostics.Process p)
    {
        try { return p.ExitCode; } catch { return null; }
    }
}
