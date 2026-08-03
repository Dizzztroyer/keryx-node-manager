using System.Diagnostics;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.Localization;

namespace KeryxNodeManager.Core.Diagnostics;

/// <summary>One system-readiness check's outcome: what was checked, whether it passed, and a
/// human-readable detail line. `Required` distinguishes a blocking check (Windows version) from an
/// informational one (WSL, Docker) that the wizard shows but never gates progress on, since
/// docs/ARCHITECTURE.md establishes NativeWindowsRuntimeBackend — not WSL, not Docker — as the
/// default and recommended path (brief §4's wizard system-check step still surfaces WSL/Docker
/// presence because brief §3 lists WSL as an alternative backend a user can opt into later).
/// </summary>
public sealed record SystemCheckResult(string Name, bool Passed, string Detail, bool Required);

/// <summary>
/// Real (not simulated) environment checks for the first-run wizard (brief §4, step 1). Every
/// check here inspects actual OS/process state — there is no hardcoded "assume it's fine" branch.
/// GPU detection reuses the same IGpuInfoProvider the rest of the app queries (brief §4 step 2 and
/// the GPU page), so the wizard can never disagree with what the app sees post-wizard.
/// </summary>
public static class SystemChecker
{
    /// <summary>Windows 10/11 required — keryxd.exe/keryx-miner.exe are Windows binaries and the
    /// app itself is WPF/net8.0-windows, so anything older is not a supported target.</summary>
    public static SystemCheckResult CheckWindowsVersion()
    {
        var os = Environment.OSVersion;
        bool ok = os.Platform == PlatformID.Win32NT && os.Version.Major >= 10;
        string detail = ok
            ? CoreStrings.Format("SystemChecker.WindowsVersionOk", os.Version)
            : CoreStrings.Format("SystemChecker.WindowsVersionTooOld", os.VersionString);
        return new SystemCheckResult(CoreStrings.Get("SystemChecker.WindowsVersionName"), ok, detail, Required: true);
    }

    /// <summary>Queries the real IGpuInfoProvider (nvidia-smi in production, the injected mock in
    /// --mock runs) rather than assuming a GPU exists — a machine with no NVIDIA driver installed
    /// must see an honest failure here, not a silently-empty GPU page later.</summary>
    public static async Task<SystemCheckResult> CheckNvidiaAsync(IGpuInfoProvider provider, CancellationToken ct = default)
    {
        try
        {
            var devices = await provider.QueryAsync(ct);
            if (devices.Count == 0)
            {
                return new SystemCheckResult(
                    CoreStrings.Get("SystemChecker.GpuName"), false,
                    CoreStrings.Get("SystemChecker.GpuNoneFound"), Required: true);
            }
            string names = string.Join(", ", devices.Select(d => d.Name));
            return new SystemCheckResult(CoreStrings.Get("SystemChecker.GpuName"), true,
                CoreStrings.Format("SystemChecker.GpuFound", names), Required: true);
        }
        catch (GpuQueryException ex)
        {
            return new SystemCheckResult(CoreStrings.Get("SystemChecker.GpuName"), false, ex.Message, Required: true);
        }
    }

    /// <summary>Informational only — brief §3/docs/ARCHITECTURE.md make NativeWindowsRuntimeBackend
    /// the default, so a missing WSL install must never block the wizard. Runs `wsl.exe --status`
    /// with a short timeout so a hung/missing binary can't stall the wizard indefinitely.</summary>
    public static async Task<SystemCheckResult> CheckWslAsync(CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo("wsl.exe", "--status")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return new SystemCheckResult(CoreStrings.Get("SystemChecker.WslName"), false,
                    CoreStrings.Get("SystemChecker.WslNotStarted"), Required: false);
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new SystemCheckResult(CoreStrings.Get("SystemChecker.WslName"), false,
                    CoreStrings.Get("SystemChecker.WslTimeout"),
                    Required: false);
            }

            bool ok = process.ExitCode == 0;
            return new SystemCheckResult(CoreStrings.Get("SystemChecker.WslName"), ok,
                ok ? CoreStrings.Get("SystemChecker.WslDetected") : CoreStrings.Get("SystemChecker.WslNotDetected"),
                Required: false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new SystemCheckResult(CoreStrings.Get("SystemChecker.WslName"), false,
                CoreStrings.Get("SystemChecker.WslNotFound"),
                Required: false);
        }
    }

    /// <summary>Informational only — the app never launches anything through Docker; this is
    /// purely a courtesy notice in case a future backend option needs it. Scans PATH directly
    /// (no process spawn needed) for docker.exe.</summary>
    public static SystemCheckResult CheckDocker()
    {
        try
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            bool found = pathVar
                .Split(Path.PathSeparator)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Any(p => File.Exists(Path.Combine(p, "docker.exe")));
            return new SystemCheckResult(CoreStrings.Get("SystemChecker.DockerName"), found,
                found ? CoreStrings.Get("SystemChecker.DockerFound") : CoreStrings.Get("SystemChecker.DockerNotFound"),
                Required: false);
        }
        catch (Exception ex)
        {
            return new SystemCheckResult(CoreStrings.Get("SystemChecker.DockerName"), false, ex.Message, Required: false);
        }
    }
}
