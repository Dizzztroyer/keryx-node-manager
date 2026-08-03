using KeryxNodeManager.Core.Secrets;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

public class SecretMaskerTests
{
    [Fact]
    public void MaskAddress_TruncatesMiddleOfAddress()
    {
        var masked = SecretMasker.MaskAddress(
            "keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte");
        Assert.StartsWith("keryx:qrxpcu", masked);
        Assert.Contains("…", masked);
        Assert.DoesNotContain("hnajuhte", masked[..^4]); // full tail shouldn't appear mid-string
    }

    [Fact]
    public void MaskLogLine_RedactsLongHexStrings()
    {
        var line = "escrow key loaded: a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4";
        var masked = SecretMasker.MaskLogLine(line);
        Assert.DoesNotContain("a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4", masked);
    }

    [Theory]
    [InlineData("Authorization: Bearer sk-abcdef1234567890")]
    [InlineData("api_key=sk-abcdef1234567890")]
    [InlineData("secret: mysupersecretvalue")]
    public void MaskLogLine_RedactsTokenLikePatterns(string line)
    {
        var masked = SecretMasker.MaskLogLine(line);
        Assert.DoesNotContain("sk-abcdef1234567890", masked);
        Assert.DoesNotContain("mysupersecretvalue", masked);
    }

    [Fact]
    public void MaskLogLine_LeavesOrdinaryLogLinesUntouched()
    {
        var line = "keryxd: block accepted at DAA 123456";
        Assert.Equal(line, SecretMasker.MaskLogLine(line));
    }
}
