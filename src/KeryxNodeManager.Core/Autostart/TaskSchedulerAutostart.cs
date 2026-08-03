using System.Diagnostics;
using System.IO;
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

    /// <summary>0.2.7 fix: a real user report ("checked the box, nothing started, and it
    /// immediately unchecked itself") plus this class's own documented root-cause finding above
    /// (some Windows installs' UAC token filtering denies schtasks /Create even for a `/RL LIMITED`
    /// per-user task, for reasons entirely outside this app's control) means RegisterAsync cannot
    /// be relied on alone for "make autostart just work for a normal user". The classic, much more
    /// universally-permitted alternative - dropping a tiny launcher into the current user's Startup
    /// folder (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup) - needs no Task Scheduler
    /// API, no elevation, and no policy edge cases: Windows Explorer itself runs anything placed
    /// there at every logon, which is exactly the same ONLOGON/RL LIMITED semantics this class
    /// already promises. This is used as a silent fallback only when schtasks fails, so a healthy
    /// machine still gets the (slightly more "proper") Task Scheduler entry as before.</summary>
    private static string StartupFolderScriptPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "KeryxNodeManager.cmd");

    public async Task RegisterAsync(string executablePath, CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await RunAsync(BuildRegisterArguments(executablePath), ct);
        if (exitCode != 0)
        {
            if (TryRegisterViaStartupFolder(executablePath))
            {
                return;
            }
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
        RemoveStartupFolderScriptIfPresent();
        if (exitCode != 0 && !stderr.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            throw new AutostartException(
                CoreStrings.Format("TaskScheduler.UnregisterFailed", exitCode, stderr.Trim()));
        }
    }

    /// <summary>Writes the Startup-folder launcher script (see StartupFolderScriptPath's doc
    /// comment) - returns false (never throws) on any failure so the caller can still surface the
    /// original schtasks error if this fallback also doesn't work out.</summary>
    private static bool TryRegisterViaStartupFolder(string executablePath)
    {
        try
        {
            // "start "" "path"" (not just the bare path) so a path containing spaces is handled
            // correctly and the batch window itself doesn't stay open waiting on the launched exe.
            var script = $"@echo off\r\nstart \"\" \"{executablePath}\"\r\n";
            File.WriteAllText(StartupFolderScriptPath, script);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveStartupFolderScriptIfPresent()
    {
        try
        {
            if (File.Exists(StartupFolderScriptPath)) File.Delete(StartupFolderScriptPath);
        }
        catch
        {
            // Best-effort cleanup only - a leftover launcher script is harmless (worst case it
            // re-launches an app that's already running, which the app's own single-instance mutex
            // already handles).
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
        return exitCode == 0 || File.Exists(StartupFolderScriptPath);
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
