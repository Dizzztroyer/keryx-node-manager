using KeryxNodeManager.Core.Process;
using KeryxNodeManager.Core.Secrets;

namespace KeryxNodeManager.Core.Logging;

/// <summary>One masked, already-persisted log line, either from the node's or the miner's
/// stdout/stderr.</summary>
public sealed record LogLine(ManagedProcessKind Kind, bool IsError, string Text, DateTimeOffset At);

/// <summary>
/// Central destination for node/miner process output (brief §12): masks every line through
/// SecretMasker before it is kept anywhere, buffers the most recent lines in memory for the Logs
/// page to display live, and appends the same masked lines to a rotating file on disk so history
/// survives an app restart and can be bundled into a diagnostic export.
///
/// This is deliberately the *only* place raw process output is allowed to reach a file or a UI
/// list - DashboardViewModel wires each ManagedProcess's launch spec to call Append() instead of
/// letting stdout/stderr go anywhere else, so there is exactly one point where the masking rule can
/// be forgotten, not one per call site.
/// </summary>
public sealed class LogSink
{
    /// <summary>How many of the most recent lines per process kind are kept in memory for the
    /// Logs page - independent of on-disk retention, which is governed by AppSettings.LogRetentionDays.</summary>
    public const int MaxBufferedLinesPerKind = 2000;

    private readonly string _logsDirectory;
    private readonly int _retentionDays;
    private readonly long _maxBytesPerFile;
    private readonly object _lock = new();
    private readonly Dictionary<ManagedProcessKind, LinkedList<LogLine>> _buffers = new()
    {
        [ManagedProcessKind.Node] = new(),
        [ManagedProcessKind.Miner] = new(),
    };

    public event Action<LogLine>? LineAppended;

    /// <summary>maxBytesPerFile is a raw byte threshold, not megabytes - see the FromMegabytes
    /// factory for the normal construction path. Kept explicit so tests can exercise file-rolling
    /// with a small threshold without needing to append megabytes of text to trigger it.</summary>
    public LogSink(string logsDirectory, int retentionDays, long maxBytesPerFile)
    {
        _logsDirectory = logsDirectory;
        _retentionDays = retentionDays;
        _maxBytesPerFile = Math.Max(1, maxBytesPerFile);
        Directory.CreateDirectory(_logsDirectory);
    }

    /// <summary>Normal construction path - matches AppSettings.MaxLogSizeMb's unit directly.</summary>
    public static LogSink FromMegabytes(string logsDirectory, int retentionDays, long maxLogSizeMb) =>
        new(logsDirectory, retentionDays, Math.Max(1, maxLogSizeMb) * 1024L * 1024L);

    public string LogsDirectory => _logsDirectory;

    public IReadOnlyList<LogLine> GetBuffered(ManagedProcessKind kind)
    {
        lock (_lock) return _buffers[kind].ToList();
    }

    /// <summary>Masks, buffers, and persists one line. Safe to call from any thread - the runtime
    /// backends invoke this directly from their OutputDataReceived/ErrorDataReceived handlers,
    /// which run on the ThreadPool, not the UI thread.</summary>
    public void Append(ManagedProcessKind kind, bool isError, string rawText)
    {
        var line = new LogLine(kind, isError, SecretMasker.MaskLogLine(rawText), DateTimeOffset.UtcNow);

        lock (_lock)
        {
            var buffer = _buffers[kind];
            buffer.AddLast(line);
            while (buffer.Count > MaxBufferedLinesPerKind) buffer.RemoveFirst();

            AppendToFile(kind, line);
        }

        LineAppended?.Invoke(line);
    }

    private void AppendToFile(ManagedProcessKind kind, LogLine line)
    {
        var path = CurrentFilePath(kind, line.At);
        var text = $"[{line.At:yyyy-MM-dd HH:mm:ss} UTC] {(line.IsError ? "ERR" : "OUT")} {line.Text}{Environment.NewLine}";
        File.AppendAllText(path, text);
    }

    /// <summary>Picks today's file for the given kind, rolling to a "-N" suffix once the current
    /// file would exceed MaxLogSizeMb - keeps any single file from growing unbounded within a day
    /// without needing a background timer.</summary>
    private string CurrentFilePath(ManagedProcessKind kind, DateTimeOffset at)
    {
        var prefix = kind == ManagedProcessKind.Node ? "keryxd" : "keryx-miner";
        var datePart = at.UtcDateTime.ToString("yyyy-MM-dd");
        for (var n = 0; ; n++)
        {
            var suffix = n == 0 ? "" : $"-{n}";
            var candidate = Path.Combine(_logsDirectory, $"{prefix}-{datePart}{suffix}.log");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < _maxBytesPerFile)
                return candidate;
        }
    }

    /// <summary>Deletes on-disk log files whose last-write time is older than
    /// AppSettings.LogRetentionDays. Real filesystem check against actual file timestamps, not a
    /// filename-date parse - so a file touched by an external tool still counts as "recent" rather
    /// than being pruned on its embedded date alone.</summary>
    public void PruneOldFiles(DateTimeOffset? nowUtc = null)
    {
        var cutoff = (nowUtc ?? DateTimeOffset.UtcNow).AddDays(-_retentionDays);
        if (!Directory.Exists(_logsDirectory)) return;

        foreach (var file in Directory.GetFiles(_logsDirectory, "*.log"))
        {
            DateTime lastWrite;
            try { lastWrite = File.GetLastWriteTimeUtc(file); }
            catch { continue; }

            if (lastWrite < cutoff.UtcDateTime)
            {
                try { File.Delete(file); } catch { /* best-effort - a locked file is skipped, not fatal */ }
            }
        }
    }
}
