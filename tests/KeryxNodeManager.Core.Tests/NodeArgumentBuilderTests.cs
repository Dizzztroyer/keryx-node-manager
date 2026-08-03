using KeryxNodeManager.Core.Cli;
using KeryxNodeManager.Core.Models;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers NodeArgumentBuilder's --appdir and --rpclisten-json emission. Both were real, silent
/// bugs before this increment: --appdir was plumbed through the method signature but the only
/// caller (DashboardViewModel) always passed null, so it never fired in practice; --rpclisten-json
/// didn't exist at all, meaning nothing this app could build (peer discovery, sync-status polling)
/// could ever reach keryxd's RPC surface. These tests exist so neither regresses silently again.
/// </summary>
public class NodeArgumentBuilderTests
{
    [Fact]
    public void Build_NodeDataDirectorySet_EmitsAppDir()
    {
        var profile = new MiningProfile { NodeDataDirectory = @"C:\data\keryx" };

        var args = NodeArgumentBuilder.Build(profile, appDataDir: null);

        var idx = args.IndexOf("--appdir");
        Assert.True(idx >= 0);
        Assert.Equal(@"C:\data\keryx", args[idx + 1]);
    }

    [Fact]
    public void Build_ExplicitAppDataDirParameter_OverridesProfileField()
    {
        var profile = new MiningProfile { NodeDataDirectory = @"C:\from-profile" };

        var args = NodeArgumentBuilder.Build(profile, appDataDir: @"C:\explicit-override");

        var idx = args.IndexOf("--appdir");
        Assert.Equal(@"C:\explicit-override", args[idx + 1]);
    }

    [Fact]
    public void Build_NoDataDirectoryAnywhere_OmitsAppDir()
    {
        var profile = new MiningProfile();

        var args = NodeArgumentBuilder.Build(profile, appDataDir: null);

        Assert.DoesNotContain("--appdir", args);
    }

    [Fact]
    public void Build_RpcJsonEnabledByDefault_UsesMainnetDefaultPortOnLoopback()
    {
        var profile = new MiningProfile();

        var args = NodeArgumentBuilder.Build(profile, appDataDir: null);

        Assert.Contains($"--rpclisten-json=127.0.0.1:{NodeArgumentBuilder.DefaultRpcJsonPortMainnet}", args);
    }

    [Fact]
    public void Build_TestnetEnabled_UsesTestnetDefaultRpcJsonPort()
    {
        var profile = new MiningProfile { UseTestnet = true };

        var args = NodeArgumentBuilder.Build(profile, appDataDir: null);

        Assert.Contains($"--rpclisten-json=127.0.0.1:{NodeArgumentBuilder.DefaultRpcJsonPortTestnet}", args);
    }

    [Fact]
    public void Build_ExplicitRpcJsonPort_UsedInsteadOfDefault()
    {
        var profile = new MiningProfile { NodeRpcJsonPort = 55555 };

        var args = NodeArgumentBuilder.Build(profile, appDataDir: null);

        Assert.Contains("--rpclisten-json=127.0.0.1:55555", args);
    }

    [Fact]
    public void Build_RpcJsonDisabled_OmitsFlagEntirely()
    {
        var profile = new MiningProfile { NodeRpcJsonEnabled = false };

        var args = NodeArgumentBuilder.Build(profile, appDataDir: null);

        Assert.DoesNotContain(args, a => a.StartsWith("--rpclisten-json"));
    }

    [Fact]
    public void Build_NeverBindsRpcJsonToNonLoopbackAddress()
    {
        // Defense-in-depth: even though there's currently no UI path to set a non-loopback bind
        // address, this test pins the invariant so a future edit can't accidentally widen it.
        var profile = new MiningProfile();

        var args = NodeArgumentBuilder.Build(profile, appDataDir: null);

        var rpcArg = Assert.Single(args, a => a.StartsWith("--rpclisten-json"));
        Assert.StartsWith("--rpclisten-json=127.0.0.1:", rpcArg);
    }
}
