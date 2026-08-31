using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Full-matrix tests for <see cref="RunStatusMachine"/> and the
/// <see cref="Run"/> aggregate guard. The expected table below is the spec —
/// production <see cref="RunTransitions"/> must match it pair-for-pair.
/// </summary>
public sealed class RunStatusMachineShould
{
    private static readonly IReadOnlyDictionary<RunStatus, RunStatus[]> expectedTransitions =
        new Dictionary<RunStatus, RunStatus[]>
        {
            [RunStatus.Queued] = [RunStatus.Waiting, RunStatus.Running, RunStatus.Failed, RunStatus.Cancelled, RunStatus.Escalated],
            [RunStatus.Waiting] = [RunStatus.Running, RunStatus.Failed, RunStatus.Cancelled, RunStatus.Escalated],
            [RunStatus.Running] = [RunStatus.Succeeded, RunStatus.Failed, RunStatus.Cancelled, RunStatus.Escalated],
            [RunStatus.Escalated] = [RunStatus.Running, RunStatus.Failed, RunStatus.Cancelled],
            [RunStatus.Failed] = [RunStatus.Queued],
            [RunStatus.Succeeded] = [],
            [RunStatus.Cancelled] = [],
        };

    public static TheoryData<RunStatus, RunStatus, bool> Matrix
    {
        get
        {
            var data = new TheoryData<RunStatus, RunStatus, bool>();
            foreach (var from in Enum.GetValues<RunStatus>())
            {
                foreach (var to in Enum.GetValues<RunStatus>())
                {
                    data.Add(from, to, expectedTransitions[from].Contains(to));
                }
            }

            return data;
        }
    }

    [Theory(DisplayName = "Given run statuses from/to, when CanTransition is called, then it matches the transition table")]
    [MemberData(nameof(Matrix))]
    public void MatchTransitionTable(RunStatus from, RunStatus to, bool expected)
    {
        var machine = new RunStatusMachine();

        machine.CanTransition(from, to).ShouldBe(expected);
    }

    [Fact(DisplayName = "Given a legal run transition, when EnsureTransition is called, then it does not throw")]
    public void PassLegalTransitionThroughEnsure()
    {
        var machine = new RunStatusMachine();

        machine.EnsureTransition(RunStatus.Waiting, RunStatus.Running);
    }

    [Fact(DisplayName = "Given an illegal run transition, when EnsureTransition is called, then it throws")]
    public void ThrowOnIllegalTransitionThroughEnsure()
    {
        var machine = new RunStatusMachine();

        var exception = Should.Throw<InvalidOperationException>(
            () => machine.EnsureTransition(RunStatus.Succeeded, RunStatus.Running));
        exception.Message.ShouldContain("Succeeded");
        exception.Message.ShouldContain("Running");
    }

    [Fact(DisplayName = "Given a run status, when AllowedTargets is called, then it matches the expected one-hop targets")]
    public void ReturnAllowedTargets()
    {
        var machine = new RunStatusMachine();

        foreach (var from in Enum.GetValues<RunStatus>())
        {
            machine.AllowedTargets(from).ShouldBe(expectedTransitions[from], ignoreOrder: true);
        }
    }

    [Fact(DisplayName = "Given a new run, when Create is called, then it starts queued with a fresh UUIDv7 id")]
    public void CreateRunInQueuedStatus()
    {
        var projectId = ProjectId.New();
        var now = DateTimeOffset.UtcNow;

        var run = Run.Create(projectId, now);

        run.Status.ShouldBe(RunStatus.Queued);
        run.ProjectId.ShouldBe(projectId);
        run.CreatedAt.ShouldBe(now);
        run.UpdatedAt.ShouldBe(now);
        run.Id.Value.Version.ShouldBe(7);
    }

    [Fact(DisplayName = "Given a queued run, when TransitionTo running is called, then status and updated_at change")]
    public void ApplyLegalTransitionOnAggregate()
    {
        var run = Run.Create(ProjectId.New(), DateTimeOffset.UtcNow);
        var later = DateTimeOffset.UtcNow.AddMinutes(1);

        run.TransitionTo(RunStatus.Running, later);

        run.Status.ShouldBe(RunStatus.Running);
        run.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given a terminal run, when TransitionTo is called, then the aggregate throws")]
    public void RejectIllegalTransitionOnAggregate()
    {
        var run = Run.Create(ProjectId.New(), DateTimeOffset.UtcNow);
        run.TransitionTo(RunStatus.Cancelled, DateTimeOffset.UtcNow);

        _ = Should.Throw<InvalidOperationException>(() => run.TransitionTo(RunStatus.Running, DateTimeOffset.UtcNow));
    }
}
