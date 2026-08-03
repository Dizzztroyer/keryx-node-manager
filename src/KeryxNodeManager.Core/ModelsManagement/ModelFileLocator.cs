namespace KeryxNodeManager.Core.ModelsManagement;

/// <summary>
/// Computes the on-disk path keryx-miner.exe itself expects for a given tier, and checks whether
/// a model is already installed there. This mirrors the miner's own layout convention documented
/// in docs/KERYX_RESEARCH.md §3: "&lt;models-dir&gt;/&lt;Model-Name&gt;/model.gguf" - the app must
/// use the exact same path the miner will look for, or a manually-installed model will be
/// invisible to it.
/// </summary>
public static class ModelFileLocator
{
    public const string ModelFileName = "model.gguf";

    public static string GetModelPath(string modelsDirectory, string modelDirName) =>
        Path.Combine(modelsDirectory, modelDirName, ModelFileName);

    /// <summary>True only if the final (non-.part) file exists and is non-empty. A .part file
    /// left over from an interrupted/paused download does NOT count as installed - the miner
    /// would refuse to load a truncated .gguf, so neither should this check pretend it's ready.</summary>
    public static bool IsInstalled(string modelsDirectory, string modelDirName)
    {
        var path = GetModelPath(modelsDirectory, modelDirName);
        var info = new FileInfo(path);
        return info.Exists && info.Length > 0;
    }

    public static long? GetInstalledSizeBytes(string modelsDirectory, string modelDirName)
    {
        var info = new FileInfo(GetModelPath(modelsDirectory, modelDirName));
        return info.Exists ? info.Length : null;
    }

    /// <summary>Path of the partial download for this tier - present while a download is in
    /// progress or paused, absent once it completes (ModelDownloader moves it away) or if nothing
    /// has been started yet.</summary>
    public static string GetPartialPath(string modelsDirectory, string modelDirName) =>
        GetModelPath(modelsDirectory, modelDirName) + ".part";

    public static bool HasPartialDownload(string modelsDirectory, string modelDirName) =>
        File.Exists(GetPartialPath(modelsDirectory, modelDirName));
}
