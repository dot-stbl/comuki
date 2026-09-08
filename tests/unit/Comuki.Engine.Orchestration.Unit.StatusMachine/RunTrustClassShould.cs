using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Tests for the autonomy ratchet on the <see cref="Run"/> aggregate. The
/// ratchet mutates <see cref="Run.TrustClass"/> through
/// <see cref="Run.PromoteTo"/> / <see cref="Run.DemoteTo"/>; status transitions
/// stay orthogonal — the two state machines are independent.
/// </summary>
public sealed class RunTrustClassShould
{
    [Fact(DisplayName = "Given a new run, when Create is called, then the trust class starts at Supervised")]
    public void StartAtSupervised()
    {
        var run = Run.Create(ProjectId.New(), DateTimeOffset.UtcNow);

        run.TrustClass.ShouldBe(RunTrustClass.Supervised);
    }

    [Fact(DisplayName = "Given a supervised run, when PromoteTo is called, then it moves to Pilot and updates updated_at")]
    public void PromoteSupervisedToPilot()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);
        var later = createdAt.AddSeconds(1);

        run.PromoteTo(later);

        run.TrustClass.ShouldBe(RunTrustClass.Pilot);
        run.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given a pilot run, when PromoteTo is called, then it moves to Trusted and updates updated_at")]
    public void PromotePilotToTrusted()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);
        var promotedAt = createdAt.AddSeconds(1);
        run.PromoteTo(promotedAt);

        var trustedAt = createdAt.AddSeconds(2);
        run.PromoteTo(trustedAt);

        run.TrustClass.ShouldBe(RunTrustClass.Trusted);
        run.UpdatedAt.ShouldBe(trustedAt);
    }

    [Fact(DisplayName = "Given a trusted run, when PromoteTo is called, then it stays at Trusted and does not re-stamp updated_at")]
    public void PromoteTrustedIsNoOp()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);
        var pilotAt = createdAt.AddSeconds(1);
        run.PromoteTo(pilotAt);
        var trustedAt = createdAt.AddSeconds(2);
        run.PromoteTo(trustedAt);

        run.PromoteTo(trustedAt.AddSeconds(1));

        run.TrustClass.ShouldBe(RunTrustClass.Trusted);
        run.UpdatedAt.ShouldBe(trustedAt);
    }

    [Theory(DisplayName = "Given a non-supervised run, when DemoteTo is called, then it returns to Supervised and updates updated_at")]
    [InlineData(RunTrustClass.Pilot)]
    [InlineData(RunTrustClass.Trusted)]
    public void DemoteToSupervised(RunTrustClass starting)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);
        if (starting == RunTrustClass.Pilot)
        {
            run.PromoteTo(createdAt.AddSeconds(1));
        }
        else
        {
            run.PromoteTo(createdAt.AddSeconds(1));
            run.PromoteTo(createdAt.AddSeconds(2));
        }

        run.TrustClass.ShouldBe(starting);

        var demotedAt = createdAt.AddSeconds(5);
        run.DemoteTo(demotedAt);

        run.TrustClass.ShouldBe(RunTrustClass.Supervised);
        run.UpdatedAt.ShouldBe(demotedAt);
    }

    [Fact(DisplayName = "Given a supervised run, when DemoteTo is called, then it is a no-op")]
    public void DemoteSupervisedIsNoOp()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);

        run.DemoteTo(createdAt.AddSeconds(1));

        run.TrustClass.ShouldBe(RunTrustClass.Supervised);
        run.UpdatedAt.ShouldBe(createdAt);
    }

    [Fact(DisplayName = "Given a run, when promote/demote and status transitions interleave, then the two state machines stay independent")]
    public void TrustClassAndStatusAreOrthogonal()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);

        run.PromoteTo(createdAt.AddSeconds(1));
        run.TransitionTo(RunStatus.Running, createdAt.AddSeconds(2));
        run.TransitionTo(RunStatus.Succeeded, createdAt.AddSeconds(3));
        run.PromoteTo(createdAt.AddSeconds(4));
        run.DemoteTo(createdAt.AddSeconds(5));

        run.TrustClass.ShouldBe(RunTrustClass.Supervised);
        run.Status.ShouldBe(RunStatus.Succeeded);
    }
}
