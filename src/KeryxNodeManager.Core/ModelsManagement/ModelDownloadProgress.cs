namespace KeryxNodeManager.Core.ModelsManagement;

/// <summary>Progress snapshot reported during a model download.</summary>
public sealed record ModelDownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>Null when the server didn't report Content-Length/Content-Range (rare, but the UI
    /// must handle "unknown total" rather than assume - see ModelDownloader doc comment).</summary>
    public double? PercentComplete => TotalBytes is > 0
        ? Math.Clamp(100.0 * BytesReceived / TotalBytes.Value, 0, 100)
        : null;
}
