using System.Diagnostics;
using KeryxNodeManager.Core.Process;

namespace KeryxNodeManager.Core.Runtime;

/// <summary>
/// Secondary/optional backend: runs a Linux-built keryxd/keryx-miner under WSL2 via
/// `wsl.exe -d &lt;distro&gt; -- &lt;argv...&gt;`. Not part of the first-run wizard's default path —
/// docs/ARCHITECTURE.md explains why NativeWindowsRuntimeBackend is now the default (official
/// win64 binaries exist). This backend exists for users who explicitly want to reuse a Linux-side
/// setup. Still ArgumentList-only, never a concatenated shell string passed to wsl.exe.
/// </summary>
public sealed class WslRuntimeBackend : IKeryxRuntimeBackend
{
    private readonly string _distroName;
    private readonly Dictionary<ManagedProcessHandle, System.Diagnostics.Process> _processes = new();

    public WslRuntimeBackend(string distroName = "Ubuntu-22.04")
    {
        _distroName = distroName;
    }

    public string Name => "wsl";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add("-q");
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            var output = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return p.ExitCode == 0 && output.Contains(_distroName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public Task<ManagedProcessHandle> StartNodeAsync(NodeLaunchSpec spec, CancellationToken ct = default) =>
        StartAsync(ManagedProcessKind.Node, spec.ExecutablePath, spec.Arguments, spec.EnvironmentVariables, spec.OnOutputLine);

    public Task<ManagedProcessHandle> StartMinerAsync(MinerLaunchSpec spec, CancellationToken ct = default) =>
        StartAsync(ManagedProcessKind.Miner, spec.ExecutablePath, spec.Arguments, spec.EnvironmentVariables, spec.OnOutputLine);

    private Task<ManagedProcessHandle> StartAsync(
        ManagedProcessKind kind,
        string linuxExecutablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environmentVariables,
        Action<string, bool>? onOutputLine)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(_distroName);
        psi.ArgumentList.Add("--");
        // env vars are passed as NAME=value argv tokens to `env`, not interpolated into a string,
        // so a value containing spaces/quotes cannot inject a second command inside the WSL shell.
        if (environmentVariables.Count > 0)
        {
            psi.ArgumentList.Add("env");
            foreach (var (key, value) in environmentVariables)
                psi.ArgumentList.Add($"{key}={value}");
        }
        psi.ArgumentList.Add(linuxExecutablePath);
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        var handle = new ManagedProcessHandle { Kind = kind, State = ManagedProcessState.Starting };

        process.Exited += (_, _) => handle.State = ManagedProcessState.Stopped;

        // Same reasoning as NativeWindowsRuntimeBackend: RedirectStandardOutput/Error=true above
        // means the pipes must always be drained or the wrapped process can eventually block on a
        // full OS pipe buffer, independent of whether anything wants to forward the lines anywhere.
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine?.Invoke(e.Data, false); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine?.Invoke(e.Data, true); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        handle.Pid = process.Id; // PID of the wsl.exe wrapper, not the Linux-side PID
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
        if (process.HasExited) { handle.State = ManagedProcessState.Stopped; return; }

        // wsl.exe forwards signals poorly; kill the wrapper, which in practice tears down the
        // invoked command too because it was launched as `wsl.exe -- <cmd>` without a shell in
        // between to detach it. Confirmed live against real WSL (Ubuntu-22.04): started a `sleep
        // 120` this same way, verified both the Windows-side wsl.exe PID and the Linux-side sleep
        // PID were running (`ps aux` inside WSL), killed the Windows-side wrapper's whole process
        // tree, and confirmed the Linux-side sleep process was gone afterward - PROJECT_STATUS.md
        // has the full methodology. (Caveat found during that test, unrelated to this class: a
        // wsl.exe child launched directly from a shell that is itself already nested inside another
        // automation/parent process can return instantly without ever running the command - a
        // console/handle-inheritance quirk of that host, not of wsl.exe or this code. A properly
        // detached process, i.e. any normal top-level app including this one, does not hit that.)
        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(ct);
        }
        catch { /* already exited */ }

        handle.State = ManagedProcessState.Stopped;
        lock (_processes) _processes.Remove(handle);
    }
}
