using Comuki.Modules.Procedures.Application.Admission;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;
using Comuki.Modules.Procedures.Application.Runtime.Trace.Storage;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Admission;

/// <summary>
/// Unit tests for task 4.1: the admission binder resolves the latest
/// published version and records the pin. Returns null when no procedure
/// is configured — the unpinned default remains (spec: "additive").
/// The binder also seeds the run's planned-vs-observed trace with a
/// <c>pin_recorded</c> event so the trace endpoint has something to
/// return at admission time.
/// </summary>
public sealed class ProcedureAdmissionBinderShould
{
    [Fact(DisplayName = "Given no published versions, when TryBindAsync runs, then it returns null (unpinned default)")]
    public async Task NoVersionsReturnsNullAsync()
    {
        var (binder, resolver, _, _) = CreateHarness(null);
        var runId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var pin = await binder.TryBindAsync(
            runId, projectId, "checkout-flow", TestContext.Current.CancellationToken);

        pin.ShouldBeNull();
        await resolver.Received(1).ResolveCurrentVersionIdAsync(
            projectId, "checkout-flow", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a published version, when TryBindAsync runs, then it returns the pin and records the transition")]
    public async Task PublishedVersionReturnsPinAsync()
    {
        var versionId = "abc123";
        var (binder, _, ledger, _) = CreateHarness(versionId);
        var runId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var pin = await binder.TryBindAsync(
            runId, projectId, "checkout-flow", TestContext.Current.CancellationToken);

        pin.ShouldNotBeNull();
        pin.RunId.ShouldBe(runId);
        pin.VersionId.ShouldBe(versionId);
        pin.ProcedureKey.ShouldBe("checkout-flow");
        pin.ProjectId.ShouldBe(projectId);
        pin.PinnedAt.ShouldNotBe(default);
        await ledger.Received(1).RecordAsync(
            Arg.Is<AttemptVersionTransition>(transition =>
                transition.ToVersionId == versionId
                && transition.FromVersionId == null
                && transition.ProcedureKey == "checkout-flow"),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a published version, when TryBindAsync runs, then it seeds the run's trace with the pin metadata")]
    public async Task PublishedVersionSeedsTraceAsync()
    {
        var versionId = "trace-seed-1";
        var (binder, _, _, traceStore) = CreateHarness(versionId);
        var runId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var pinnedAt = DateTimeOffset.UtcNow;

        var pin = await binder.TryBindAsync(
            runId, projectId, "checkout-flow", TestContext.Current.CancellationToken);

        pin.ShouldNotBeNull();
        await traceStore.Received(1).SeedAsync(
            runId,
            versionId,
            "checkout-flow",
            projectId,
            Arg.Is<DateTimeOffset>(at => at > pinnedAt.AddSeconds(-5) && at <= DateTimeOffset.UtcNow.AddSeconds(5)),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given no published versions, when TryBindAsync runs, then no trace is seeded")]
    public async Task NoVersionsDoesNotSeedTraceAsync()
    {
        var (binder, _, _, traceStore) = CreateHarness(null);
        var runId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        await binder.TryBindAsync(
            runId, projectId, "checkout-flow", TestContext.Current.CancellationToken);

        await traceStore.DidNotReceiveWithAnyArgs().SeedAsync(
            default, default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    private static (ProcedureAdmissionBinder Binder, IAttemptPinResolver Resolver, IAttemptPinLedger Ledger, IProcedureTraceStore TraceStore) CreateHarness(
        string? latestVersionId)
    {
        var resolver = Substitute.For<IAttemptPinResolver>();
        resolver.ResolveCurrentVersionIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(latestVersionId);
        var ledger = Substitute.For<IAttemptPinLedger>();
        var traceStore = Substitute.For<IProcedureTraceStore>();
        var clock = TimeProvider.System;
        return (new ProcedureAdmissionBinder(resolver, ledger, traceStore, clock), resolver, ledger, traceStore);
    }
}
