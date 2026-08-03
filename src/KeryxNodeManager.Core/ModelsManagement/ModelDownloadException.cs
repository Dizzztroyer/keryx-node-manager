using KeryxNodeManager.Core.Localization;

namespace KeryxNodeManager.Core.ModelsManagement;

public class ModelDownloadException : Exception
{
    public ModelDownloadException(string message) : base(message) { }
    public ModelDownloadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when a completed download's SHA-256 doesn't match the hash the user supplied.
/// The partial file is deleted before this is thrown - a corrupt/tampered download must never be
/// left where ModelFileLocator would report it as "installed".</summary>
public sealed class ModelChecksumMismatchException : ModelDownloadException
{
    public ModelChecksumMismatchException(string expectedHex, string actualHex)
        : base(CoreStrings.Format("ModelDownload.ChecksumMismatch", expectedHex, actualHex))
    {
    }
}
