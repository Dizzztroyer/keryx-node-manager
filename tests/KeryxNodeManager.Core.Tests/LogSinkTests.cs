using KeryxNodeManager.Core.Logging;
using KeryxNodeManager.Core.Process;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// LogSink is the single point every line of node/miner stdout/stderr must pass through before it
/// is buffered for the Logs page or written to disk (brief §12) - these tests focus on the three
/// properties that matter most: secrets never reach disk unmasked, per-kind buffers stay isolated
/// from each other, and old files actually get deleted by real timestamp, not by parsing a
/// filename.
/// </summary>
public class LogSinkTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "keryx-logsink-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Append_MasksSecretBeforeBufferingOrWriting()
    {
        var dir = NewTempDir();
        try
        {
            var sink = new LogSink(dir, retentionDays: 14, maxBytesPerFile: 10 * 1024 * 1024);
            sink.Append(ManagedProcessKind.Node, isError: false,
                "auth failed, token=abcdef0123456789abcdef0123456789 rejected");

            var buffered = sink.GetBuffered(ManagedProcessKind.Node);
            Assert.Single(buffered);
            Assert.DoesNotContain("abcdef0123456789abcdef0123456789", buffered[0].Text);

            var fileText = File.ReadAllText(Directory.GetFiles(dir, "keryxd-*.log").Single());
            Assert.DoesNotContain("abcdef0123456789abcdef0123456789", fileText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Append_KeepsNodeAndMinerBuffersIndependent()
    {
        var dir = NewTempDir();
        try
        {
            var sink = new LogSink(dir, retentionDays: 14, maxBytesPerFile: 10 * 1024 * 1024);
            sink.Append(ManagedProcessKind.Node, false, "node line");
            sink.Append(ManagedProcessKind.Miner, false, "miner line 1");
            sink.Append(ManagedProcessKind.Miner, false, "miner line 2");

            Assert.Single(sink.GetBuffered(ManagedProcessKind.Node));
            Assert.Equal(2, sink.GetBuffered(ManagedProcessKind.Miner).Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Append_RollsToSuffixedFileOnceCurrentFileExceedsMaxBytes()
    {
        var dir = NewTempDir();
        try
        {
            // A tiny byte threshold so a couple of short lines are enough to trigger rolling,
            // without needing to append megabytes of text in a unit test.
            var sink = new LogSink(dir, retentionDays: 14, maxBytesPerFile: 50);
            for (int i = 0; i < 20; i++)
            {
                sink.Append(ManagedProcessKind.Node, false, $"line number {i} of test output");
            }

            var files = Directory.GetFiles(dir, "keryxd-*.log");
            Assert.True(files.Length > 1, "expected at least one rolled-over file once the first exceeded the byte cap");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PruneOldFiles_DeletesOnlyFilesOlderThanRetention()
    {
        var dir = NewTempDir();
        try
        {
            var oldFile = Path.Combine(dir, "keryxd-2020-01-01.log");
            var recentFile = Path.Combine(dir, "keryxd-2026-08-01.log");
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(recentFile, "recent");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddHours(-1));

            var sink = new LogSink(dir, retentionDays: 14, maxBytesPerFile: 10 * 1024 * 1024);
            sink.PruneOldFiles();

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(recentFile));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Append_RaisesLineAppendedEvent()
    {
        var dir = NewTempDir();
        try
        {
            var sink = new LogSink(dir, retentionDays: 14, maxBytesPerFile: 10 * 1024 * 1024);
            LogLine? received = null;
            sink.LineAppended += l => received = l;

            sink.Append(ManagedProcessKind.Miner, isError: true, "share rejected");

            Assert.NotNull(received);
            Assert.Equal(ManagedProcessKind.Miner, received!.Kind);
            Assert.True(received.IsError);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
