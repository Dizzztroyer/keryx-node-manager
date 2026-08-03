using KeryxNodeManager.Core.Autostart;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers only the pure Build*Arguments command-construction logic - actually registering a Task
/// Scheduler entry requires a real Windows Task Scheduler and is verified live instead (see
/// PROJECT_STATUS.md), matching the existing NvidiaSmiGpuInfoProvider.ParseCsv /
/// SystemChecker.CheckWslAsync test-split pattern already used in this project.
/// </summary>
public class TaskSchedulerAutostartTests
{
    [Fact]
    public void BuildRegisterArguments_UsesTaskNameAndExecutablePathAsSeparateArgs()
    {
        var args = TaskSchedulerAutostart.BuildRegisterArguments(@"C:\Program Files\Keryx\KeryxNodeManager.exe");

        Assert.Equal("/Create", args[0]);
        Assert.Contains(TaskSchedulerAutostart.TaskName, args);
        // The path must be its own array element (relying on ProcessStartInfo.ArgumentList to
        // quote it correctly at the Win32 command-line level) rather than manually
        // quoted/embedded into a combined string - a manually-quoted path would end up
        // double-quoted once ArgumentList escapes it again.
        Assert.Contains(@"C:\Program Files\Keryx\KeryxNodeManager.exe", args);
        Assert.DoesNotContain(args, a => a.Contains('"'));
    }

    [Fact]
    public void BuildRegisterArguments_RunsAtLogonWithoutElevation()
    {
        var args = TaskSchedulerAutostart.BuildRegisterArguments(@"C:\app.exe");

        Assert.Contains("/SC", args);
        Assert.Contains("ONLOGON", args);
        Assert.Contains("/RL", args);
        Assert.Contains("LIMITED", args);
    }

    [Fact]
    public void BuildRegisterArguments_ForcesOverwriteOfExistingTask()
    {
        var args = TaskSchedulerAutostart.BuildRegisterArguments(@"C:\app.exe");

        Assert.Contains("/F", args);
    }

    [Fact]
    public void BuildUnregisterArguments_TargetsSameTaskNameAndForcesDelete()
    {
        var args = TaskSchedulerAutostart.BuildUnregisterArguments();

        Assert.Equal(new[] { "/Delete", "/TN", TaskSchedulerAutostart.TaskName, "/F" }, args);
    }

    [Fact]
    public void BuildQueryArguments_TargetsSameTaskName()
    {
        var args = TaskSchedulerAutostart.BuildQueryArguments();

        Assert.Equal(new[] { "/Query", "/TN", TaskSchedulerAutostart.TaskName }, args);
    }

    [Fact]
    public void TaskName_IsStableAcrossAllThreeCommands()
    {
        // All three commands must agree on the exact same task name, or a register/query/delete
        // cycle would silently operate on different tasks. Asserted explicitly rather than just
        // trusted, since TaskName is a compile-time constant any future edit could change in one
        // spot and miss another.
        Assert.Contains(TaskSchedulerAutostart.TaskName, TaskSchedulerAutostart.BuildRegisterArguments("x"));
        Assert.Contains(TaskSchedulerAutostart.TaskName, TaskSchedulerAutostart.BuildUnregisterArguments());
        Assert.Contains(TaskSchedulerAutostart.TaskName, TaskSchedulerAutostart.BuildQueryArguments());
    }
}
