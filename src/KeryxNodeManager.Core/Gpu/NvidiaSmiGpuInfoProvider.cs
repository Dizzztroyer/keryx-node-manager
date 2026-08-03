using System.Diagnostics;
using System.Globalization;
using KeryxNodeManager.Core.Localization;
using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.Gpu;

/// <summary>
/// Queries real hardware via `nvidia-smi --query-gpu=... --format=csv,noheader,nounits`.
/// The CSV-parsing logic is a separate static method (ParseCsv) so it is unit-testable without
/// nvidia-smi installed — tests feed it captured sample output.
/// </summary>
public sealed class NvidiaSmiGpuInfoProvider : IGpuInfoProvider
{
    // Order matters: must match the --query-gpu field list exactly.
    private const string QueryFields =
        "index,uuid,name,memory.total,memory.used,utilization.gpu,temperature.gpu," +
        "power.draw,power.limit,clocks.sm,clocks.mem,fan.speed,driver_version,compute_cap";

    private readonly string _executablePath;

    public NvidiaSmiGpuInfoProvider(string executablePath = "nvidia-smi")
    {
        _executablePath = executablePath;
    }

    public async Task<IReadOnlyList<GpuDevice>> QueryAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add($"--query-gpu={QueryFields}");
        psi.ArgumentList.Add("--format=csv,noheader,nounits");

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new GpuQueryException(
                CoreStrings.Get("Gpu.NvidiaSmiNotFound"), ex);
        }

        string stdout = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new GpuQueryException(CoreStrings.Format("Gpu.NvidiaSmiFailed", process.ExitCode, stderr));
        }

        return ParseCsv(stdout);
    }

    /// <summary>
    /// Parses nvidia-smi CSV rows matching QueryFields' column order. Missing/non-numeric fields
    /// (e.g. "[N/A]" for fan speed on headless server GPUs) degrade gracefully to null/0 rather
    /// than throwing, since a single unreadable field must not hide the whole GPU from the UI.
    /// </summary>
    public static IReadOnlyList<GpuDevice> ParseCsv(string csv)
    {
        var result = new List<GpuDevice>();
        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length < 14) continue;

            result.Add(new GpuDevice
            {
                CudaIndex = ParseInt(parts[0]) ?? result.Count,
                Uuid = parts[1],
                Name = parts[2],
                TotalVramMb = ParseLong(parts[3]) ?? 0,
                UsedVramMb = ParseLong(parts[4]) ?? 0,
                UtilizationPercent = ParseInt(parts[5]) ?? 0,
                TemperatureC = ParseInt(parts[6]) ?? 0,
                PowerDrawW = ParseDouble(parts[7]) ?? 0,
                PowerLimitW = ParseDouble(parts[8]) ?? 0,
                CoreClockMhz = ParseInt(parts[9]) ?? 0,
                MemoryClockMhz = ParseInt(parts[10]) ?? 0,
                FanSpeedPercent = ParseInt(parts[11]),
                DriverVersion = parts[12],
                ComputeCapability = string.IsNullOrWhiteSpace(parts[13]) ? null : parts[13],
            });
        }
        // nvidia-smi's own row order is already CUDA/driver order, but sort defensively by index
        // in case a future field addition reorders columns unexpectedly.
        return result.OrderBy(g => g.CudaIndex).ToList();
    }

    private static int? ParseInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static long? ParseLong(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}

public sealed class GpuQueryException : Exception
{
    public GpuQueryException(string message, Exception? inner = null) : base(message, inner) { }
}
