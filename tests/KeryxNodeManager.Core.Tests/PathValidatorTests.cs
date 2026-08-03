using KeryxNodeManager.Core.Validation;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

public class PathValidatorTests
{
    [Fact]
    public void Validate_RejectsEmptyPath()
    {
        var result = PathValidator.Validate("");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsRelativePath()
    {
        var result = PathValidator.Validate("models");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsAbsoluteTempPath()
    {
        var result = PathValidator.Validate(Path.Combine(Path.GetTempPath(), "keryx-models"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsPathsContainingInvalidCharacters()
    {
        var invalidChar = Path.GetInvalidPathChars().FirstOrDefault();
        if (invalidChar == default) return; // platform has no invalid path chars to test with
        var result = PathValidator.Validate(Path.Combine(Path.GetTempPath(), $"bad{invalidChar}path"));
        Assert.False(result.IsValid);
    }
}
