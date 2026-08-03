using KeryxNodeManager.Core.Localization;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers CoreStrings' actual contract (never throw, real fallback behaviour, language switching
/// visibly changes output) rather than re-asserting every one of its ~35 translated strings -
/// those are exercised indirectly by whichever Core-layer test already checks a message contains
/// expected placeholder values (e.g. ProfileStoreTests, TaskSchedulerAutostartTests), which
/// implicitly run against whatever CoreStrings.Language happens to be at the time (defaulting to
/// "ru" unless a previous test in the same process changed it - see the reset in Dispose below).
/// </summary>
public class CoreStringsTests : IDisposable
{
    // CoreStrings.Language is static/global mutable state shared across the whole test assembly.
    // xUnit can run tests in the same collection sequentially but doesn't guarantee ordering
    // between test classes, so a test here setting Language to "en" could otherwise leak into an
    // unrelated test elsewhere that assumes the "ru" default (e.g. any test asserting on an exact
    // Cyrillic exception message). Capturing/restoring it here keeps this test class's own
    // language-switching assertions from being a hidden source of flakiness for the rest of the suite.
    private readonly string _originalLanguage = CoreStrings.Language;

    public void Dispose() => CoreStrings.Language = _originalLanguage;

    [Fact]
    public void Get_DefaultsToRussian()
    {
        CoreStrings.Language = "ru";
        Assert.Equal("Путь не может быть пустым.", CoreStrings.Get("Path.Empty"));
    }

    [Fact]
    public void Get_SwitchesLiveWithLanguageProperty()
    {
        CoreStrings.Language = "en";
        Assert.Equal("The path cannot be empty.", CoreStrings.Get("Path.Empty"));

        CoreStrings.Language = "ru";
        Assert.Equal("Путь не может быть пустым.", CoreStrings.Get("Path.Empty"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("uk")]
    public void Get_EveryNonRussianLanguageHasNoMissingKeys(string language)
    {
        // Every key present in the Russian dictionary (the always-complete baseline every other
        // language is translated from) must also resolve to a *different-looking* real
        // translation in every other supported language, not silently fall back to Russian text -
        // a missing translation should be caught here, at build/test time, not discovered later by
        // a user switching languages and still seeing Cyrillic.
        CoreStrings.Language = "ru";
        var russianKeys = new[]
        {
            "TaskScheduler.AccessDeniedHint", "TaskScheduler.RegisterFailed", "TaskScheduler.UnregisterFailed",
            "Profile.NotFound", "Profile.NameEmpty", "Profile.AlreadyExists", "Profile.CannotDeleteLast",
            "SystemChecker.WindowsVersionName", "SystemChecker.WindowsVersionOk", "SystemChecker.WindowsVersionTooOld",
            "SystemChecker.GpuName", "SystemChecker.GpuNoneFound", "SystemChecker.GpuFound",
            "SystemChecker.WslName", "SystemChecker.WslNotStarted", "SystemChecker.WslTimeout",
            "SystemChecker.WslDetected", "SystemChecker.WslNotDetected", "SystemChecker.WslNotFound",
            "SystemChecker.DockerName", "SystemChecker.DockerFound", "SystemChecker.DockerNotFound",
            "Gpu.NvidiaSmiNotFound", "Gpu.NvidiaSmiFailed",
            "Tier.ExcludedInsufficientVram", "Tier.AutoAssigned", "Tier.ManualRisky", "Tier.ManualFits",
            "ModelDownload.ChecksumMismatch",
            "Process.AlreadyRunning", "Process.NodeStarted", "Process.MinerStarted", "Process.StoppedByUser",
            "Process.RestartLimitReached", "Process.RestartingSoon", "Process.Restarted", "Process.RestartFailed",
            "Runtime.ExecutableNotFound",
            "Safety.Critical", "Safety.Warning", "Safety.Normal",
            "Path.Empty", "Path.InvalidChars", "Path.NotAbsolute", "Path.InvalidPath", "Path.ProtectedRoot",
        };

        CoreStrings.Language = language;
        foreach (var key in russianKeys)
        {
            CoreStrings.Language = "ru";
            string russian = CoreStrings.Get(key);
            CoreStrings.Language = language;
            string translated = CoreStrings.Get(key);
            Assert.False(
                string.Equals(russian, translated, StringComparison.Ordinal),
                $"Key '{key}' has no real '{language}' translation - it's falling back to Russian text.");
        }
    }

    [Fact]
    public void Get_UnknownKey_ReturnsKeyItselfRatherThanThrowing()
    {
        Assert.Equal("Totally.Bogus.Key", CoreStrings.Get("Totally.Bogus.Key"));
    }

    [Fact]
    public void Format_SubstitutesArgumentsIntoLookedUpString()
    {
        CoreStrings.Language = "en";
        Assert.Equal("Executable not found: C:\\fake.exe",
            CoreStrings.Format("Runtime.ExecutableNotFound", "C:\\fake.exe"));
    }
}
