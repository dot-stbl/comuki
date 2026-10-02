using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Editions;
using Comuki.Modules.Procedures.Domain.Exceptions;
using Comuki.Modules.Procedures.Domain.Kinds;
using Comuki.Modules.Procedures.Domain.Kinds.Types;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Editions;

/// <summary>
/// Unit tests for task 1.3: editions enforcement at kind granularity.
/// The gate refuses publication when a kind's paid feature key is not
/// granted by the effective edition (spec scenario: "Two-approval gate
/// on community"). Kinds without a feature key always pass. The gate is
/// structurally separate from the catalog validator — it does not run on
/// every catalog lookup, only at compile time. A pinned version stays
/// executable under the edition that compiled it even after a downgrade
/// (spec scenario: "Paid kind inside an active pin after downgrade").
/// </summary>
public sealed class NodeKindEditionsGateShould
{
    /// <summary>The reference feature key the spec calls out: a two-approval gate is paid-only.</summary>
    private const string TwoApprovalFeature = "procedures.two-approval-gates";

    /// <summary>A paid descriptor that carries the two-approval feature key.</summary>
    private static NodeKindDescriptor PaidDescriptor(string featureKey = TwoApprovalFeature)
    {
        return new NodeKindDescriptor(
            Key: "human-gate",
            Title: "Human gate",
            Description: "Two-approval decision kind.",
            OwnerSurface: NodeKindOwnerSurface.Decision,
            ParameterSchema: "human-gate-config-v2",
            OutcomePorts: [
                OutcomePort.Define("gate.approved", "Approved."),
                OutcomePort.Define("gate.rejected", "Rejected."),
            ],
            EvidenceRequirements: ["decision-record"],
            RiskClass: "high",
            Idempotency: NodeKindIdempotency.Inherent,
            ApprovalFloor: 2,
            EditionsFeatureKey: featureKey);
    }

    /// <summary>A free descriptor that carries no editions feature key.</summary>
    private static NodeKindDescriptor FreeDescriptor()
    {
        return new(
            Key: "intake",
            Title: "Intake",
            Description: "Accepts an inbound ticket.",
            OwnerSurface: NodeKindOwnerSurface.WorkItem,
            ParameterSchema: "intake-envelope-v1",
            OutcomePorts: [
                OutcomePort.Define("accepted", "Intake accepted."),
                OutcomePort.Define("rejected", "Intake rejected."),
            ],
            EvidenceRequirements: ["intake-record"],
            RiskClass: "low",
            Idempotency: NodeKindIdempotency.Inherent,
            ApprovalFloor: 0,
            EditionsFeatureKey: null);
    }

    [Fact(DisplayName = "Given a paid kind, when validated under community (empty grants), then refused with the kind and feature key named")]
    public void PaidKindRefusedUnderCommunity()
    {
        var entries = new[] { new NodeKindCatalogEntry("human-gate", PaidDescriptor()) };

        var exception = Should.Throw<ProcedureNodeKindsDomainException>(
            () => NodeKindEditionsGate.ValidateEntries(entries, GrantedFeatureKeys.Empty));

        exception.Code.ShouldBe(ProcedureNodeKindsDomainException.FeatureKeyNotGranted);
        exception.Message.ShouldContain("human-gate");
        exception.Message.ShouldContain(TwoApprovalFeature);
    }

    [Fact(DisplayName = "Given a paid kind, when validated under an enterprise edition that grants the feature, then allowed")]
    public void PaidKindAllowedUnderEnterprise()
    {
        var entries = new[] { new NodeKindCatalogEntry("human-gate", PaidDescriptor()) };

        Should.NotThrow(() =>
            NodeKindEditionsGate.ValidateEntries(entries, GrantedFeatureKeys.Of(TwoApprovalFeature)));
    }

    [Fact(DisplayName = "Given a free kind (no feature key), when validated under community, then allowed (no opt-in, no check)")]
    public void KindWithoutFeatureKeyAlwaysAllowed()
    {
        var entries = new[] { new NodeKindCatalogEntry("intake", FreeDescriptor()) };

        Should.NotThrow(() =>
            NodeKindEditionsGate.ValidateEntries(entries, GrantedFeatureKeys.Empty));
    }

    [Fact(DisplayName = "Given a catalog previously compiled under enterprise, when the edition lapses to community, the pinned catalog continues to resolve its entries (no runtime edition check on lookup)")]
    public void PinnedCatalogNotRecheckedAtLookup()
    {
        // Establish the pin as it would have been: enterprise edition,
        // published catalog carrying the paid descriptor. The pin is a
        // catalog object that the runtime holds and looks up against.
        var entries = new[] { new NodeKindCatalogEntry("human-gate", PaidDescriptor()) };
        var pinned = new NodeKindCatalog("1.0", "git:enterprise@abc123", entries);

        // A NEW compilation under community refuses the paid kind —
        // what the future compile gate will do at publication time.
        Should.Throw<ProcedureNodeKindsDomainException>(
            () => NodeKindEditionsGate.ValidateEntries(entries, GrantedFeatureKeys.Empty));

        // The pin itself, however, is data: NodeKindCatalog carries no
        // edition and Find() never calls the gate. It still resolves the
        // paid kind because the run was executing when the edition was
        // granted; a downgrade does not retroactively change the pin.
        // Spec scenario: "Paid kind inside an active pin after downgrade".
        pinned.Version.ShouldBe("1.0");
        pinned.Find("human-gate").ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a paid descriptor mixed with free descriptors under enterprise, when validated, then only the paid descriptor passes the feature check and the batch returns")]
    public void MixedBatchPassesUnderEnterprise()
    {
        var entries = new[]
        {
            new NodeKindCatalogEntry("intake", FreeDescriptor()),
            new NodeKindCatalogEntry("human-gate", PaidDescriptor()),
        };

        Should.NotThrow(() =>
            NodeKindEditionsGate.ValidateEntries(entries, GrantedFeatureKeys.Of(TwoApprovalFeature)));
    }
}
