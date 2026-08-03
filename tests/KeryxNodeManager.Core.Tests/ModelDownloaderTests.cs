using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using KeryxNodeManager.Core.ModelsManagement;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Exercises ModelDownloader against an in-process fake HttpMessageHandler (no real network) -
/// covers the behaviours the Models page depends on: fresh download, checksum verification (both
/// outcomes), HTTP Range-based resume, and that cancelling mid-transfer leaves a usable partial
/// file rather than corrupting or losing it (this is what "Pause" on the real UI relies on).
/// </summary>
public class ModelDownloaderTests
{
    private static readonly byte[] SampleData = Enumerable.Range(0, 10_000).Select(i => (byte)(i % 251)).ToArray();

    [Fact]
    public async Task DownloadAsync_FreshDownload_WritesExactBytesToDestination()
    {
        var dir = CreateTempDir();
        try
        {
            var destination = Path.Combine(dir, "model.gguf");
            var downloader = new ModelDownloader(new HttpClient(new FakeRangeHandler(SampleData)));

            await downloader.DownloadAsync(new Uri("http://fake.local/model.gguf"), destination, progress: null, CancellationToken.None);

            Assert.True(File.Exists(destination));
            Assert.Equal(SampleData, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".part")); // moved away, not left behind
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_CorrectChecksum_Succeeds()
    {
        var dir = CreateTempDir();
        try
        {
            var destination = Path.Combine(dir, "model.gguf");
            var downloader = new ModelDownloader(new HttpClient(new FakeRangeHandler(SampleData)));
            var expectedHex = Convert.ToHexString(SHA256.HashData(SampleData)).ToLowerInvariant();

            await downloader.DownloadAsync(
                new Uri("http://fake.local/model.gguf"), destination, progress: null, CancellationToken.None, expectedHex);

            Assert.True(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_WrongChecksum_ThrowsAndDeletesPartialFile_LeavesNoDestinationFile()
    {
        var dir = CreateTempDir();
        try
        {
            var destination = Path.Combine(dir, "model.gguf");
            var downloader = new ModelDownloader(new HttpClient(new FakeRangeHandler(SampleData)));
            const string wrongHex = "0000000000000000000000000000000000000000000000000000000000000000";

            await Assert.ThrowsAsync<ModelChecksumMismatchException>(() =>
                downloader.DownloadAsync(
                    new Uri("http://fake.local/model.gguf"), destination, progress: null, CancellationToken.None, wrongHex));

            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".part")); // corrupt/mismatched download must not linger
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_ResumesFromExistingPartialFile_ViaHttpRange()
    {
        var dir = CreateTempDir();
        try
        {
            var destination = Path.Combine(dir, "model.gguf");
            var partialPath = destination + ".part";
            var alreadyDownloaded = SampleData.Take(4_000).ToArray();
            await File.WriteAllBytesAsync(partialPath, alreadyDownloaded);

            var handler = new FakeRangeHandler(SampleData);
            var downloader = new ModelDownloader(new HttpClient(handler));

            await downloader.DownloadAsync(new Uri("http://fake.local/model.gguf"), destination, progress: null, CancellationToken.None);

            Assert.Equal(4_000, handler.LastRequestedRangeStart);
            Assert.Equal(SampleData, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_ServerIgnoresRange_RestartsFromZeroInsteadOfCorrupting()
    {
        var dir = CreateTempDir();
        try
        {
            var destination = Path.Combine(dir, "model.gguf");
            var partialPath = destination + ".part";
            // Stale/mismatched partial bytes from a different source - if the resume logic
            // blindly appended the server's full-file response to this, the result would be
            // corrupt (SampleData prefixed with garbage).
            await File.WriteAllBytesAsync(partialPath, new byte[] { 9, 9, 9, 9 });

            var handler = new FakeRangeHandler(SampleData) { SupportsRange = false };
            var downloader = new ModelDownloader(new HttpClient(handler));

            await downloader.DownloadAsync(new Uri("http://fake.local/model.gguf"), destination, progress: null, CancellationToken.None);

            Assert.Equal(SampleData, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_CancellationMidTransfer_LeavesPartialFile_NotDestinationFile()
    {
        var dir = CreateTempDir();
        try
        {
            var destination = Path.Combine(dir, "model.gguf");
            var handler = new FakeRangeHandler(SampleData) { ThrottleChunkDelayMs = 30, ThrottleChunkSize = 512 };
            var downloader = new ModelDownloader(new HttpClient(handler));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(90));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                downloader.DownloadAsync(new Uri("http://fake.local/model.gguf"), destination, progress: null, cts.Token));

            Assert.False(File.Exists(destination));
            Assert.True(File.Exists(destination + ".part"));
            var partialLength = new FileInfo(destination + ".part").Length;
            Assert.True(partialLength > 0 && partialLength < SampleData.Length,
                $"expected a partial write strictly between 0 and {SampleData.Length}, got {partialLength}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KeryxModelDownloaderTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Fake server: honors Range (returning 206 + Content-Range) unless SupportsRange is
    /// false (in which case it always returns the whole file with 200, as some plain static file
    /// hosts do). ThrottleChunkDelayMs/ThrottleChunkSize simulate a slow connection so a
    /// cancellation test has time to fire mid-transfer without a real network.</summary>
    private sealed class FakeRangeHandler : HttpMessageHandler
    {
        private readonly byte[] _data;
        public bool SupportsRange { get; init; } = true;
        public int? ThrottleChunkDelayMs { get; init; }
        public int ThrottleChunkSize { get; init; } = 4096;
        public long LastRequestedRangeStart { get; private set; }

        public FakeRangeHandler(byte[] data) => _data = data;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            long start = 0;
            var requestedRange = request.Headers.Range?.Ranges.FirstOrDefault();
            if (SupportsRange && requestedRange?.From is long from)
            {
                start = from;
                LastRequestedRangeStart = from;
            }

            var slice = _data.Skip((int)start).ToArray();
            HttpContent content = ThrottleChunkDelayMs is int delayMs
                ? new ThrottledContent(slice, delayMs, ThrottleChunkSize)
                : new ByteArrayContent(slice);

            HttpResponseMessage response;
            if (start > 0 && SupportsRange)
            {
                response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, _data.Length - 1, _data.Length);
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }

            response.Content.Headers.ContentLength = slice.Length;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// HttpContent that hands back a Stream yielding small delayed chunks on each ReadAsync call,
    /// so a CancellationToken passed all the way down to that Stream.ReadAsync has a real window
    /// to fire mid-copy - without a real socket. This must override CreateContentReadStreamAsync
    /// rather than SerializeToStreamAsync: HttpContent's default ReadAsStreamAsync(ct)
    /// implementation calls SerializeToStreamAsync into a MemoryStream and only returns once that
    /// finishes, which would buffer the whole throttled transfer before ModelDownloader's own
    /// copy loop (and its cancellation checks) ever ran.
    /// </summary>
    private sealed class ThrottledContent : HttpContent
    {
        private readonly byte[] _data;
        private readonly int _delayMs;
        private readonly int _chunkSize;

        public ThrottledContent(byte[] data, int delayMs, int chunkSize)
        {
            _data = data;
            _delayMs = delayMs;
            _chunkSize = chunkSize;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new ThrottledReadStream(_data, _delayMs, _chunkSize));

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new ThrottledReadStream(_data, _delayMs, _chunkSize));

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await stream.WriteAsync(_data);

        protected override bool TryComputeLength(out long length)
        {
            length = _data.Length;
            return true;
        }
    }

    /// <summary>Read-only stream that releases <see cref="_chunkSize"/> bytes per ReadAsync call,
    /// after an artificial delay - the delay is where a passed-in CancellationToken actually gets
    /// a chance to fire, mid-transfer, exactly like a slow real network read would behave.</summary>
    private sealed class ThrottledReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _delayMs;
        private readonly int _chunkSize;
        private int _position;

        public ThrottledReadStream(byte[] data, int delayMs, int chunkSize)
        {
            _data = data;
            _delayMs = delayMs;
            _chunkSize = chunkSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _data.Length) return 0;
            await Task.Delay(_delayMs, cancellationToken);
            var toCopy = Math.Min(Math.Min(_chunkSize, buffer.Length), _data.Length - _position);
            _data.AsSpan(_position, toCopy).CopyTo(buffer.Span);
            _position += toCopy;
            return toCopy;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
