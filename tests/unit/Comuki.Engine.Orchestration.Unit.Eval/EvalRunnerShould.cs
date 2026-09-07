using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Unit.Eval.Eval;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// Unit tests for <see cref="EvalRunner"/>: synthetic tasks driving
/// <see cref="Run"/> +
/// <see cref="WorkItem"/>
/// state machines; pass/fail scoring against the expected outcome.
/// </summary>
public sealed class EvalRunnerShould
{
    private static readonly DateTimeOffset fixedNow = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a happy-path run task, when Run is called, then it passes with a clean log")]
    public void PassHappyRunPath()
    {
        var task = new EvalTask(
            Id: "t1",
            Name: "Queued -> Running -> Succeeded",
            Kind: EvalTaskKind.Run,
            Operations:
            [
                new EvalOperation(EvalAction.Create, string.Empty),
                new EvalOperation(EvalAction.Transition, "Running"),
                new EvalOperation(EvalAction.Transition, "Succeeded"),
            ],
            Expected: new EvalExpected(
                FinalStatus: "Succeeded",
                TransitionLog: ["Queued", "Running", "Succeeded"]));

        var result = EvalRunner.Run(task, fixedNow);

        result.Passed.ShouldBeTrue();
        result.Mismatches.ShouldBeEmpty();
        result.ActualTransitionLog.ShouldBe(["Queued", "Running", "Succeeded"]);
    }

    [Fact(DisplayName = "Given a run task whose final status mismatches, when Run is called, then the result carries one mismatch")]
    public void FailOnFinalStatusMismatch()
    {
        var task = new EvalTask(
            Id: "t1",
            Name: "Expecting Succeeded but runner lands Failed",
            Kind: EvalTaskKind.Run,
            Operations:
            [
                new EvalOperation(EvalAction.Create, string.Empty),
                new EvalOperation(EvalAction.Transition, "Running"),
                new EvalOperation(EvalAction.Transition, "Failed"),
            ],
            Expected: new EvalExpected(
                FinalStatus: "Succeeded",
                TransitionLog: ["Queued", "Running", "Failed"]));

        var result = EvalRunner.Run(task, fixedNow);

        result.Passed.ShouldBeFalse();
        result.Mismatches.ShouldHaveSingleItem();
        result.Mismatches[0].Field.ShouldBe("final-status");
        result.Mismatches[0].Expected.ShouldBe("Succeeded");
        result.Mismatches[0].Actual.ShouldBe("Failed");
    }

    [Fact(DisplayName = "Given a transition-log mismatch, when Run is called, then the runner reports the full diff")]
    public void FailOnTransitionLogMismatch()
    {
        var task = new EvalTask(
            Id: "t1",
            Name: "transition log drift",
            Kind: EvalTaskKind.Run,
            Operations:
            [
                new EvalOperation(EvalAction.Create, string.Empty),
                new EvalOperation(EvalAction.Transition, "Running"),
                new EvalOperation(EvalAction.Transition, "Cancelled"),
            ],
            Expected: new EvalExpected(
                FinalStatus: "Cancelled",
                TransitionLog: ["Queued", "Running", "Failed", "Queued", "Running", "Cancelled"]));

        var result = EvalRunner.Run(task, fixedNow);

        result.Passed.ShouldBeFalse();
        result.Mismatches.ShouldHaveSingleItem();
        result.Mismatches[0].Field.ShouldBe("transition-log");
        result.Mismatches[0].Expected.ShouldContain("Failed");
        result.Mismatches[0].Actual.ShouldNotContain("Failed");
    }

    [Fact(DisplayName = "Given a terminal-run transition that's illegal, when Run is called, then the runner matches the failure message")]
    public void PassNegativePathWhenGuardFires()
    {
        var task = new EvalTask(
            Id: "t1",
            Name: "Succeeded -> Running is illegal",
            Kind: EvalTaskKind.Run,
            Operations:
            [
                new EvalOperation(EvalAction.Create, string.Empty),
                new EvalOperation(EvalAction.Transition, "Running"),
                new EvalOperation(EvalAction.Transition, "Succeeded"),
                new EvalOperation(EvalAction.TransitionExpectFailure, "Running"),
            ],
            Expected: new EvalExpected(
                FinalStatus: string.Empty,
                TransitionLog: [],
                ExpectsFailure: true,
                ExpectedFailureMessage: "Succeeded"));

        var result = EvalRunner.Run(task, fixedNow);

        result.Passed.ShouldBeTrue();
        result.ActualTransitionLog[^1].ShouldBe("Succeeded");
    }

    [Fact(DisplayName = "Given an expected failure that doesn't fire, when Run is called, then the runner reports a missing-exception mismatch")]
    public void FailNegativePathWhenNoException()
    {
        var task = new EvalTask(
            Id: "t1",
            Name: "Expects failure but op succeeds",
            Kind: EvalTaskKind.Run,
            Operations:
            [
                new EvalOperation(EvalAction.Create, string.Empty),
                new EvalOperation(EvalAction.Transition, "Running"),
            ],
            Expected: new EvalExpected(
                FinalStatus: string.Empty,
                TransitionLog: [],
                ExpectsFailure: true,
                ExpectedFailureMessage: "Succeeded"));

        var result = EvalRunner.Run(task, fixedNow);

        result.Passed.ShouldBeFalse();
        result.Mismatches.ShouldContain(static mismatch => mismatch.Field == "expects-failure");
    }

    [Fact(DisplayName = "Given a work-item lease-cycle task, when Run is called, then the runner captures every transition including heartbeat")]
    public void PassWorkItemLeaseCycle()
    {
        var task = new EvalTask(
            Id: "wi-lease",
            Name: "Queued -> lease -> heartbeat*2 -> release -> re-lease -> Succeeded",
            Kind: EvalTaskKind.WorkItem,
            Operations:
            [
                new EvalOperation(EvalAction.Create, "Queued"),
                new EvalOperation(EvalAction.AssignLease, string.Empty),
                new EvalOperation(EvalAction.Heartbeat, string.Empty),
                new EvalOperation(EvalAction.Heartbeat, string.Empty),
                new EvalOperation(EvalAction.ReleaseLease, string.Empty),
                new EvalOperation(EvalAction.AssignLease, string.Empty),
                new EvalOperation(EvalAction.Transition, "Succeeded"),
            ],
            Expected: new EvalExpected(
                FinalStatus: "Succeeded",
                TransitionLog: ["Queued", "Running", "Running", "Running", "Queued", "Running", "Succeeded"]));

        var result = EvalRunner.Run(task, fixedNow);

        result.Passed.ShouldBeTrue();
        result.Mismatches.ShouldBeEmpty();
        result.ActualTransitionLog.Count.ShouldBe(7);
    }

    [Fact(DisplayName = "Given a work-item Blocked -> Running lease path, when Run is called, then the runner honours the entry status")]
    public void PassWorkItemBlockedEntry()
    {
        var task = new EvalTask(
            Id: "wi-blocked",
            Name: "Blocked -> Queued -> Running",
            Kind: EvalTaskKind.WorkItem,
            Operations:
            [
                new EvalOperation(EvalAction.Create, "Blocked"),
                new EvalOperation(EvalAction.Transition, "Queued"),
                new EvalOperation(EvalAction.AssignLease, string.Empty),
            ],
            Expected: new EvalExpected(
                FinalStatus: "Running",
                TransitionLog: ["Blocked", "Queued", "Running"]));

        var result = EvalRunner.Run(task, fixedNow);

        result.Passed.ShouldBeTrue();
    }
}
