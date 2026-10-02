using Comuki.Modules.Procedures.Application.Admission;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Admission;

/// <summary>
/// Unit tests for task 4.1: the admission binder resolves the latest
/// published version and records the pin. Returns null when no procedure
/// is configured — the unpinned default remains (spec: "additive").
/// </summary>
public sealed class ProcedureAdmissionBinderShould
{
    [Fact(DisplayName = "Given no published versions, when TryBindAsync runs, then it returns null (unpinned default)")]
    public async Task NoVersionsReturnsNullAsync()
    {
        var (binder, resolver, _) = CreateHarness(null);
        var projectId = Guid.NewGuid();

        var pin = await binder.TryBindAsync(projectId, "checkout-flow", TestContext.Current.CancellationToken);

        pin.ShouldBeNull();
        await resolver.Received(1).ResolveCurrentVersionIdAsync(
            projectId, "checkout-flow", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a published version, when TryBindAsync runs, then it returns the pin and records the transition")]
    public async Task PublishedVersionReturnsPinAsync()
    {
        var versionId = "abc123";
        var (binder, _, ledger) = CreateHarness(versionId);
        var projectId = Guid.NewGuid();

        var pin = await binder.TryBindAsync(projectId, "checkout-flow", TestContext.Current.CancellationToken);

        pin.ShouldNotBeNull();
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

    private static (ProcedureAdmissionBinder Binder, IAttemptPinResolver Resolver, IAttemptPinLedger Ledger) CreateHarness(
        string? latestVersionId)
    {
        var resolver = Substitute.For<IAttemptPinResolver>();
        resolver.ResolveCurrentVersionIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(latestVersionId);
        var ledger = Substitute.For<IAttemptPinLedger>();
        var clock = TimeProvider.System;
        return (new ProcedureAdmissionBinder(resolver, ledger, clock), resolver, ledger);
    }
}
