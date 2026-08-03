using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Updates;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers DefaultInstallPaths - the zero-input defaults that replaced hard "укажите путь к..."
/// requirements for keryxd.exe/keryx-miner.exe/the models folder (brief follow-up, 2026-08-03).
/// Mostly guards against silent path-shape regressions: everything must live under the same
/// %LocalAppData%\KeryxNodeManager root already used for settings/logs (see ConfigStore,
/// LogSink), and the two executable kinds must never collide on the same file.
/// </summary>
public class DefaultInstallPathsTests
{
    [Fact]
    public void BinDirectory_And_ModelsDirectory_LiveUnderSameLocalAppDataRoot()
    {
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeryxNodeManager");

        Assert.StartsWith(expectedRoot, DefaultInstallPaths.BinDirectory);
        Assert.StartsWith(expectedRoot, DefaultInstallPaths.ModelsDirectory);
    }

    [Fact]
    public void ExecutablePathFor_NodeAndMiner_AreDistinctAndUseRealExeNames()
    {
        var nodePath = DefaultInstallPaths.ExecutablePathFor(ManagedBinaryKind.Node);
        var minerPath = DefaultInstallPaths.ExecutablePathFor(ManagedBinaryKind.Miner);

        Assert.NotEqual(nodePath, minerPath);
        Assert.EndsWith("keryxd.exe", nodePath);
        Assert.EndsWith("keryx-miner.exe", minerPath);
        Assert.StartsWith(DefaultInstallPaths.BinDirectory, nodePath);
        Assert.StartsWith(DefaultInstallPaths.BinDirectory, minerPath);
    }
}
