using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using KeryxNodeManager.Core.Localization;

namespace KeryxNodeManager.Core.Autostart;

/// <summary>
/// Registers/unregisters a per-user "run at logon" Task Scheduler entry via schtasks.exe (brief
/// §11: launch KeryxNodeManager.exe itself with Windows). This is a distinct concern from
/// MiningProfile.AutoStartNode/AutoStartMiner, which only govern whether the *already-running*
/// app also launches keryxd.exe/keryx-miner.exe - see AppSettings.StartWithWindows, which this
/// class is the real implementation behind.
///
/// schtasks.exe (not the raw COM ITaskService API) is used because a per-user ONLOGON task needs
/// no elevation and no COM interop, matching this project's existing "spawn the real CLI tool,
/// parse its real exit code/output" pattern already used for nvidia-smi (NvidiaSmiGpuInfoProvider)
/// and wsl.exe (SystemChecker.CheckWslAsync).
///
/// The Build*Arguments methods are separated from the process-spawning methods purely so the
/// command construction is unit-testable without a real Windows Task Scheduler (same split as
/// NvidiaSmiGpuInfoProvider.ParseCsv vs QueryAsync).
/// </summary>
public sealed class TaskSchedulerAutostart
{
    public const string TaskName = "KeryxNodeManager_Autostart";

    private readonly string _schtasksPath;
    private static readonly Encoding ConsoleOutputEncoding = ResolveOemEncoding();

    public TaskSchedulerAutostart(string schtasksPath = "schtasks.exe")
    {
        _schtasksPath = schtasksPath;
    }

    /// <summary>
    /// schtasks.exe writes its (localized, e.g. Cyrillic) error text to the console using the
    /// system's OEM codepage (e.g. 866 on a Russian-locale Windows install), not UTF-8. .NET's
    /// default StandardErrorEncoding for a redirected stream does not match that, which turned a
    /// real "Access is denied" error into unreadable mojibake in front of a real user during this
    /// project's own live verification of this exact feature - caught by actually reading the
    /// resulting error message on the real machine, not assumed correct from a passing unit test.
    /// GetOEMCP is Windows-only; on any other OS (the Core project builds cross-platform for CI)
    /// this simply falls back to UTF-8, which is never exercised there anyway since schtasks.exe
    /// itself only exists on Windows.
    /// </summary>
    private static Encoding ResolveOemEncoding()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return Encoding.UTF8;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)GetOEMCP());
        }
        catch
        {
            // Any failure resolving the exact codepage should degrade to readable-ish UTF-8
            // rather than throw during static initialization and take the whole class down.
            return Encoding.UTF8;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    /// <summary>
    /// /SC ONLOGON: runs at the current user's next logon, not a fixed clock time - no admin
    /// rights required to register this for the current user. /RL LIMITED: runs with standard
    /// (non-elevated) rights, since the app itself never needs elevation to launch
    /// keryxd/keryx-miner. /F: overwrite silently if a task with this name already exists, so
    /// re-registering (e.g. after the exe was reinstalled to a new path) updates it in place
    /// instead of failing with "task already exists".
    /// </summary>
    public static IReadOnlyList<string> BuildRegisterArguments(string executablePath) => new[]
    {
        "/Create", "/TN", TaskName, "/TR", executablePath, "/SC", "ONLOGON", "/RL", "LIMITED", "/F",
    };

    public static IReadOnlyList<string> BuildUnregisterArguments() => new[]
    {
        "/Delete", "/TN", TaskName, "/F",
    };

    public static IReadOnlyList<string> BuildQueryArguments() => new[]
    {
        "/Query", "/TN", TaskName,
    };

    public async Task RegisterAsync(string executablePath, CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await RunAsync(BuildRegisterArguments(executablePath), ct);
        if (exitCode != 0)
        {
            // "Access is denied"/"Отказано в доступе" here was originally guessed to be
            // antivirus/EDR interference (this project's own test machine had Norton 360
            // installed) - that guess was never confirmed and turned out to be wrong. Root-caused
            // for real on that same machine (PROJECT_STATUS.md, thirty-fifth increment):
            // schtasks /Create failed identically from a non-elevated PowerShell session even
            // though the current user account was a full local Administrator, and succeeded
            // immediately from a UAC-elevated session with the exact same arguments - no
            // Defender/EDR block event existed anywhere in the event log for any attempt.
            // The real cause is this Windows install's UAC token filtering: an Administrator's
            // day-to-day process token runs at Medium integrity with the Administrators group
            // filtered out, and this particular machine's Task Scheduler policy denies /Create for
            // that filtered token even for a `/RL LIMITED` per-user task (which, per Microsoft's
            // own docs, normally shouldn't need elevation) - a real Windows/Task-Scheduler
            // behaviour, not a bug in this class or interference from third-party software.
            // NOTE: this literal is a locale-detection heuristic against schtasks.exe's own OEM
            // output, not a user-facing message - it must stay Russian regardless of
            // CoreStrings.Language, since it's matching text schtasks itself printed.
            bool looksLikeAccessDenied =
                stderr.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("отказано", StringComparison.OrdinalIgnoreCase);
            string hint = looksLikeAccessDenied
                ? CoreStrings.Get("TaskScheduler.AccessDeniedHint")
                : "";
            throw new AutostartException(
                CoreStrings.Format("TaskScheduler.RegisterFailed", exitCode, stderr.Trim(), hint));
        }
    }

    /// <summary>
    /// Idempotent: if the task is already absent, schtasks /Delete exits non-zero with an
    /// "ERROR: The system cannot find the file specified." message - that specific case is
    /// treated as success rather than surfaced as a failure, since the caller's intent ("make sure
    /// autostart is off") is already satisfied.
    /// </summary>
    public async Task UnregisterAsync(CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await RunAsync(BuildUnregisterArguments(), ct);
        if (exitCode != 0 && !stderr.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            throw new AutostartException(
                CoreStrings.Format("TaskScheduler.UnregisterFailed", exitCode, stderr.Trim()));
        }
    }

    /// <summary>
    /// Queries real Task Scheduler state rather than trusting the persisted
    /// AppSettings.StartWithWindows flag - a hand-edited settings.json, or the exe having moved
    /// since the task was registered, must not make the Settings page show a checked box that
    /// doesn't correspond to anything actually scheduled.
    /// </summary>
    public async Task<bool> IsRegisteredAsync(CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunAsync(BuildQueryArguments(), ct);
        return exitCode == 0;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _schtasksPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleOutputEncoding,
            StandardErrorEncoding = ConsoleOutputEncoding,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start();
        string stdout = await process.StandardOutput.ReadToEndAsync(ct);
        string stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, stdout, stderr);
    }
}

public sealed class AutostartException : Exception
{
    public AutostartException(string message) : base(message) { }
}
