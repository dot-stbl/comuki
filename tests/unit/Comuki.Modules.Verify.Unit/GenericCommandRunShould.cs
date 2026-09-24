using Comuki.Modules.Verify.Domain.Exceptions;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Verify.Unit;

/// <summary>
/// <see cref="GenericCommandRun"/> lifecycle: creation validation, the
/// legal Pending → Running → (Green|Red) transitions, and the illegal
/// ones the domain refuses.
/// </summary>
public sealed class GenericCommandRunShould
{
    private static readonly DateTimeOffset anchorTime = DateTimeOffset.Parse(
        "2026-09-23T00:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact(DisplayName = "Given valid inputs, when Create is called, then the run starts Pending with the given executable and arguments")]
    public void CreatePendingRun()
    {
        var run = GenericCommandRun.Create(
            new ProjectId(Guid.CreateVersion7()),
            "general",
            "dotnet",
            ["build", "comuki.slnx"],
            expectedExitCode: 0,
            anchorTime);

        run.Status.ShouldBe(GenericCommandStatus.Pending);
        run.Executable.ShouldBe("dotnet");
        run.Arguments.ShouldBe(["build", "comuki.slnx"]);
        run.ExpectedExitCode.ShouldBe(0);
        run.StartedAt.ShouldBeNull();
        run.FinishedAt.ShouldBeNull();
        run.CreatedAt.ShouldBe(anchorTime);
    }

    [Fact(DisplayName = "Given a blank executable, when Create is called, then it throws")]
    public void ThrowWhenExecutableIsBlank()
    {
        Should.Throw<ArgumentException>(static () => GenericCommandRun.Create(
            null,
            "general",
            "   ",
            [],
            expectedExitCode: 0,
            anchorTime));
    }

    [Fact(DisplayName = "Given a null project, when Create is called, then it creates a global gate run")]
    public void CreateGlobalGateRunWithNullProject()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "echo", ["ok"], 0, anchorTime);

        run.ProjectId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a Pending run, when MarkRunning is called, then it stamps StartedAt and moves to Running")]
    public void MarkRunningFromPending()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);

        run.MarkRunning(anchorTime.AddSeconds(1));

        run.Status.ShouldBe(GenericCommandStatus.Running);
        run.StartedAt.ShouldBe(anchorTime.AddSeconds(1));
    }

    [Fact(DisplayName = "Given a run that is not Pending, when MarkRunning is called, then it throws")]
    public void ThrowWhenMarkRunningFromNonPending()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);
        run.MarkRunning(anchorTime);

        var exception = Should.Throw<IllegalGenericCommandStatusTransitionException>(() => run.MarkRunning(anchorTime));
        exception.From.ShouldBe(GenericCommandStatus.Running);
        exception.To.ShouldBe(GenericCommandStatus.Running);
    }

    [Fact(DisplayName = "Given a Running run, when MarkCompleted matches the expected exit code, then it becomes Green")]
    public void MarkCompletedGreenOnMatchingExitCode()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], expectedExitCode: 0, anchorTime);
        run.MarkRunning(anchorTime);

        run.MarkCompleted(0, "[out] 10.0.303\n", anchorTime.AddSeconds(2));

        run.Status.ShouldBe(GenericCommandStatus.Green);
        run.ActualExitCode.ShouldBe(0);
        run.OutputLog.ShouldBe("[out] 10.0.303\n");
        run.FinishedAt.ShouldBe(anchorTime.AddSeconds(2));
    }

    [Fact(DisplayName = "Given a Running run, when MarkCompleted does not match the expected exit code, then it becomes Red")]
    public void MarkCompletedRedOnMismatchedExitCode()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["build"], expectedExitCode: 0, anchorTime);
        run.MarkRunning(anchorTime);

        run.MarkCompleted(1, "[err] failed\n", anchorTime.AddSeconds(2));

        run.Status.ShouldBe(GenericCommandStatus.Red);
        run.ActualExitCode.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a run that is not Running, when MarkCompleted is called, then it throws")]
    public void ThrowWhenMarkCompletedFromNonRunning()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);

        Should.Throw<IllegalGenericCommandStatusTransitionException>(
            () => run.MarkCompleted(0, string.Empty, anchorTime));
    }

    [Fact(DisplayName = "Given a Running run, when MarkLaunchFailed is called, then it becomes Red and keeps StartedAt")]
    public void MarkLaunchFailedFromRunning()
    {
        // The worker always calls MarkRunning before invoking the runner
        // (GenericCommandVerifierWorker.PollOnceAsync) — MarkLaunchFailed
        // is therefore only ever legal from Running, never from Pending.
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);
        run.MarkRunning(anchorTime);

        run.MarkLaunchFailed("launch failed: file not found", anchorTime.AddSeconds(1));

        run.Status.ShouldBe(GenericCommandStatus.Red);
        run.ActualExitCode.ShouldBeNull();
        run.OutputLog.ShouldBe("launch failed: file not found");
        run.StartedAt.ShouldBe(anchorTime);
        run.FinishedAt.ShouldBe(anchorTime.AddSeconds(1));
    }

    [Fact(DisplayName = "Given a Pending run, when MarkLaunchFailed is called, then it throws")]
    public void ThrowWhenMarkLaunchFailedFromPending()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);

        Should.Throw<IllegalGenericCommandStatusTransitionException>(
            () => run.MarkLaunchFailed("boom", anchorTime));
    }
}
