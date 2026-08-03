using KeryxNodeManager.Core.Cli;
using KeryxNodeManager.Core.Models;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

public class MinerArgumentBuilderTests
{
    private static MiningProfile BaseProfile() => new()
    {
        MiningAddress = "keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte",
        NodeEndpoint = "127.0.0.1",
    };

    [Fact]
    public void Build_ThrowsWithoutMiningAddress()
    {
        var profile = BaseProfile();
        profile.MiningAddress = "";
        Assert.Throws<InvalidOperationException>(
            () => MinerArgumentBuilder.Build(profile, Array.Empty<ModelTier?>(), false));
    }

    [Fact]
    public void Build_IncludesRequiredMiningAddressAndKeryxdAddress()
    {
        var args = MinerArgumentBuilder.Build(BaseProfile(), Array.Empty<ModelTier?>(), false);
        Assert.Contains("--mining-address", args);
        int i = args.IndexOf("--mining-address");
        Assert.Equal(BaseProfile().MiningAddress, args[i + 1]);
        Assert.Contains("--keryxd-address", args);
    }

    [Fact]
    public void Build_OmitsForceModelWhenAllAuto()
    {
        var args = MinerArgumentBuilder.Build(BaseProfile(), new ModelTier?[] { null, null }, anyManualOverride: false);
        Assert.DoesNotContain("--force-model", args);
    }

    [Fact]
    public void Build_EmitsForceModelInCudaDriverOrderWhenManualOverridesExist()
    {
        var assignments = new ModelTier?[] { ModelTier.Light, ModelTier.VeryHigh };
        var args = MinerArgumentBuilder.Build(BaseProfile(), assignments, anyManualOverride: true);
        int i = args.IndexOf("--force-model");
        Assert.True(i >= 0);
        Assert.Equal("light,very-high", args[i + 1]);
    }

    [Fact]
    public void Build_NeverProducesASingleConcatenatedCommandString()
    {
        // Regression guard for command-injection safety (brief §20): every argument must be its
        // own list element, never containing embedded shell metacharacters glued to other args.
        var profile = BaseProfile();
        profile.ExtraMinerArguments.Add("--debug");
        var args = MinerArgumentBuilder.Build(profile, Array.Empty<ModelTier?>(), false);
        Assert.All(args, a => Assert.DoesNotContain(" --", a)); // no args smuggled via whitespace-joining
    }

    [Fact]
    public void Build_ExtraArgumentsAreAppendedAsSeparateTokens()
    {
        var profile = BaseProfile();
        profile.ExtraMinerArguments.Add("--debug");
        profile.ExtraMinerArguments.Add("--threads=2");
        var args = MinerArgumentBuilder.Build(profile, Array.Empty<ModelTier?>(), false);
        Assert.Contains("--debug", args);
        Assert.Contains("--threads=2", args);
    }

    [Fact]
    public void BuildCudaVisibleDevices_JoinsIndexesInOrder()
    {
        var value = MinerArgumentBuilder.BuildCudaVisibleDevices(new[] { 0, 2 });
        Assert.Equal("0,2", value);
    }
}
