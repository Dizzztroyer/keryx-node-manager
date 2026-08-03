using KeryxNodeManager.Core.Validation;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

public class KeryxAddressValidatorTests
{
    // Real devfund address hardcoded in keryx-miner's src/cli.rs - used here as a known-good sample.
    private const string RealDevfundAddress =
        "keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte";

    [Fact]
    public void LooksValid_AcceptsRealDevfundAddress()
    {
        Assert.True(KeryxAddressValidator.LooksValid(RealDevfundAddress));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-an-address")]
    [InlineData("bitcoin:abc123")]
    [InlineData("keryx:")]
    [InlineData("keryx:short")]
    [InlineData("keryx:HasUppercaseCharsXXXXXXXXXXXXXXXXXXX")]
    [InlineData("keryx:contains1andbwhichareinvalidbech32charsxx")]
    public void LooksValid_RejectsMalformedAddresses(string? address)
    {
        Assert.False(KeryxAddressValidator.LooksValid(address));
    }

    [Fact]
    public void LooksValid_AcceptsTestnetPrefix()
    {
        Assert.True(KeryxAddressValidator.LooksValid(
            "keryxtest:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte"));
    }

    [Fact]
    public void GetNetworkPrefix_ExtractsPrefixBeforeColon()
    {
        Assert.Equal("keryx", KeryxAddressValidator.GetNetworkPrefix(RealDevfundAddress));
    }
}
