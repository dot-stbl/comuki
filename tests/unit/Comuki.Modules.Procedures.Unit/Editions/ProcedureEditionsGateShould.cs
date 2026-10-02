using Comuki.Modules.Procedures.Application.Editions;
using Comuki.Modules.Procedures.Domain.Editions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Editions;

/// <summary>
/// Unit tests for tasks 6.1-6.2: the procedure editions gate enforces
/// the community caps (3 published, 5 concurrent runs), the multi-repo
/// feature key, and the read-only degradation on expiry.
/// </summary>
public sealed class ProcedureEditionsGateShould
{
    [Fact(DisplayName = "Given 2 published of limit 3, when CheckPublishedLimit runs, then it passes")]
    public void UnderPublishedLimitPasses()
    {
        Should.NotThrow(static () => ProcedureEditionsGate.CheckPublishedLimit(2, 3));
    }

    [Fact(DisplayName = "Given 3 published of limit 3, when CheckPublishedLimit runs, then it refuses with the limit named")]
    public void AtPublishedLimitRefuses()
    {
        var exception = Should.Throw<ProcedureEditionsException>(
            static () => ProcedureEditionsGate.CheckPublishedLimit(3, 3));

        exception.Code.ShouldBe(ProcedureEditionsException.PublishedLimitExceeded);
        exception.Message.ShouldContain("3");
    }

    [Fact(DisplayName = "Given 10 published with no limit (paid), when CheckPublishedLimit runs, then it passes")]
    public void NoLimitPasses()
    {
        Should.NotThrow(static () => ProcedureEditionsGate.CheckPublishedLimit(10, null));
    }

    [Fact(DisplayName = "Given 4 pinned runs of limit 5, when CheckConcurrentPinnedRuns runs, then it passes")]
    public void UnderConcurrentLimitPasses()
    {
        Should.NotThrow(static () => ProcedureEditionsGate.CheckConcurrentPinnedRuns(4, 5));
    }

    [Fact(DisplayName = "Given 5 pinned runs of limit 5, when CheckConcurrentPinnedRuns runs, then it refuses")]
    public void AtConcurrentLimitRefuses()
    {
        var exception = Should.Throw<ProcedureEditionsException>(
            static () => ProcedureEditionsGate.CheckConcurrentPinnedRuns(5, 5));

        exception.Code.ShouldBe(ProcedureEditionsException.ConcurrentRunsLimitExceeded);
    }

    [Fact(DisplayName = "Given 1 repository binding, when CheckMultiRepoBinding runs, then it always passes (single-repo is free)")]
    public void SingleRepoAlwaysPasses()
    {
        Should.NotThrow(static () =>
            ProcedureEditionsGate.CheckMultiRepoBinding(1, GrantedFeatureKeys.Empty));
    }

    [Fact(DisplayName = "Given 3 repository bindings under community, when CheckMultiRepoBinding runs, then it refuses with the feature key named")]
    public void MultiRepoUnderCommunityRefuses()
    {
        var exception = Should.Throw<ProcedureEditionsException>(
            static () => ProcedureEditionsGate.CheckMultiRepoBinding(3, GrantedFeatureKeys.Empty));

        exception.Code.ShouldBe(ProcedureEditionsException.MultiRepoNotGranted);
        exception.Message.ShouldContain("procedures.multi-repo");
    }

    [Fact(DisplayName = "Given 3 repository bindings with the multi-repo key granted, when CheckMultiRepoBinding runs, then it passes")]
    public void MultiRepoWithKeyPasses()
    {
        Should.NotThrow(static () =>
            ProcedureEditionsGate.CheckMultiRepoBinding(3, GrantedFeatureKeys.Of("procedures.multi-repo")));
    }
}

/// <summary>Unit tests for task 6.2: read-only degradation on expiry.</summary>
public sealed class EditionDegradationGateShould
{
    [Fact(DisplayName = "Given an expired edition, when CheckCanCompile runs, then it refuses")]
    public void ExpiredEditionRefusesCompile()
    {
        var exception = Should.Throw<ProcedureEditionsException>(
            static () => EditionDegradationGate.CheckCanCompile(true));

        exception.Code.ShouldBe(ProcedureEditionsException.EditionExpiredReadOnly);
        exception.Message.ShouldContain("expired");
    }

    [Fact(DisplayName = "Given a valid edition, when CheckCanCompile runs, then it passes")]
    public void ValidEditionAllowsCompile()
    {
        Should.NotThrow(static () => EditionDegradationGate.CheckCanCompile(false));
    }

    [Fact(DisplayName = "Given an expired edition, when CheckCanAdmit runs, then it refuses")]
    public void ExpiredEditionRefusesAdmission()
    {
        var exception = Should.Throw<ProcedureEditionsException>(
            static () => EditionDegradationGate.CheckCanAdmit(true));

        exception.Code.ShouldBe(ProcedureEditionsException.EditionExpiredReadOnly);
    }

    [Fact(DisplayName = "Given a valid edition, when CheckCanAdmit runs, then it passes")]
    public void ValidEditionAllowsAdmission()
    {
        Should.NotThrow(static () => EditionDegradationGate.CheckCanAdmit(false));
    }
}
