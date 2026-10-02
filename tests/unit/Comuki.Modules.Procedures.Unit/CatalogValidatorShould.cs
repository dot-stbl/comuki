using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Exceptions;
using Comuki.Modules.Procedures.Domain.Kinds;
using Comuki.Modules.Procedures.Domain.Kinds.Types;
using Comuki.Modules.Procedures.Domain.Validation;
using Comuki.Modules.Procedures.Domain.Validation.Ports;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit;

/// <summary>
/// Unit tests for task 1.2: catalog validation, version-bump algorithm,
/// and the refusal of retraction when a kind is still referenced by a
/// published procedure (spec scenario: "Retraction blocked by
/// reference"). The validator's wire-contract diff protects the compiled
/// procedure's catalog pin — a new catalog never silently rewrites the
/// pin a run is executing against (spec scenario: "Republished procedure
/// keeps its catalog pin").
/// </summary>
public sealed class CatalogValidatorShould
{
    private static readonly ProcedureKindReference sampleReference =
        new(ProcedureId: "proc-001", ProcedureName: "demo-procedure");

    /// <summary>Build a baseline descriptor for tests. Defaults match the verify kind from control-plane.</summary>
    private static NodeKindDescriptor VerifyDescriptor(
        string? title = null,
        IReadOnlyList<OutcomePort>? ports = null,
        string? parameterSchema = null,
        IReadOnlyList<string>? evidenceRequirements = null,
        NodeKindOwnerSurface? ownerSurface = null,
        NodeKindIdempotency? idempotency = null,
        int? approvalFloor = null)
    {
        return new NodeKindDescriptor(
            Key: "verify",
            Title: title ?? "Verify",
            Description: "Runs a typed verifier against an artifact.",
            OwnerSurface: ownerSurface ?? NodeKindOwnerSurface.BrokerOperation,
            ParameterSchema: parameterSchema ?? "verify-call-v1",
            OutcomePorts: ports ?? [
                OutcomePort.Define("passed", "Verifier ran and the artifact passed."),
                OutcomePort.Define("failed", "Verifier ran and the artifact failed."),
                OutcomePort.Define("inconclusive", "Verifier ran but produced no verdict."),
                OutcomePort.Define("infrastructure-error", "Verifier never ran."),
            ],
            EvidenceRequirements: evidenceRequirements ?? ["verifier-report"],
            RiskClass: "low",
            Idempotency: idempotency ?? NodeKindIdempotency.Inherent,
            ApprovalFloor: approvalFloor ?? 0,
            EditionsFeatureKey: null);
    }

    /// <summary>Build a baseline catalog wrapping the given entries.</summary>
    private static NodeKindCatalog Catalog(
        string version,
        IReadOnlyList<NodeKindCatalogEntry> entries,
        string sourceRef = "test")
    {
        return new NodeKindCatalog(version, sourceRef, entries);
    }

    [Fact(DisplayName = "Given a previous is null, when ValidateAsync runs, then the first publish assigns version 1.0")]
    public async Task FirstPublishAssignsBaselineVersionAsync()
    {
        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);
        var entries = new[] { new NodeKindCatalogEntry("verify", VerifyDescriptor()) };

        var published = await validator.ValidateAsync(
            sourceRef: "git:abc123",
            draftEntries: entries,
            previous: null,
            TestContext.Current.CancellationToken);

        published.Version.ShouldBe("1.0");
        await lookup.DidNotReceiveWithAnyArgs().FindReferencesAsync(null!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a draft that retracts a kind with published references, when ValidateAsync runs, then it is refused and the referencing procedures are named")]
    public async Task RetractionBlockedByPublishedProcedureAsync()
    {
        var verify = new NodeKindCatalogEntry("verify", VerifyDescriptor());
        var repair = new NodeKindCatalogEntry(
            "repair-boundary",
            new NodeKindDescriptor(
                Key: "repair-boundary",
                Title: "Repair boundary",
                Description: "A bounded re-execution wrapper.",
                OwnerSurface: NodeKindOwnerSurface.WorkItem,
                ParameterSchema: "repair-boundary-config-v1",
                OutcomePorts: [
                    OutcomePort.Define("closed", "Boundary satisfied."),
                    OutcomePort.Define("exhausted", "Generations exhausted."),
                ],
                EvidenceRequirements: ["generation-record"],
                RiskClass: "medium",
                Idempotency: NodeKindIdempotency.Required,
                ApprovalFloor: 0,
                EditionsFeatureKey: null));

        var previous = Catalog("1.0", [verify, repair]);
        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        lookup.FindReferencesAsync("repair-boundary", TestContext.Current.CancellationToken)
            .Returns([sampleReference]);

        var validator = new CatalogValidator(lookup);

        var exception = await Should.ThrowAsync<ProcedureNodeKindsDomainException>(
            () => validator.ValidateAsync(
                sourceRef: "git:def456",
                draftEntries: [verify],
                previous: previous,
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureNodeKindsDomainException.RetractionBlocked);
        exception.Message.ShouldContain("repair-boundary");
        exception.Message.ShouldContain("demo-procedure");
    }

    [Fact(DisplayName = "Given a draft that retracts a kind without references, when ValidateAsync runs, then publication succeeds")]
    public async Task RetractionAllowedWhenNotReferencedAsync()
    {
        var verify = new NodeKindCatalogEntry("verify", VerifyDescriptor());
        var repair = new NodeKindCatalogEntry(
            "repair-boundary",
            new NodeKindDescriptor(
                Key: "repair-boundary",
                Title: "Repair boundary",
                Description: "A bounded re-execution wrapper.",
                OwnerSurface: NodeKindOwnerSurface.WorkItem,
                ParameterSchema: "repair-boundary-config-v1",
                OutcomePorts: [
                    OutcomePort.Define("closed", "Boundary satisfied."),
                    OutcomePort.Define("exhausted", "Generations exhausted."),
                ],
                EvidenceRequirements: ["generation-record"],
                RiskClass: "medium",
                Idempotency: NodeKindIdempotency.Required,
                ApprovalFloor: 0,
                EditionsFeatureKey: null));

        var previous = Catalog("1.0", [verify, repair]);
        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        lookup.FindReferencesAsync("repair-boundary", TestContext.Current.CancellationToken)
            .Returns([]);

        var validator = new CatalogValidator(lookup);

        var published = await validator.ValidateAsync(
            sourceRef: "git:ghi789",
            draftEntries: [verify],
            previous: previous,
            TestContext.Current.CancellationToken);

        published.Version.ShouldBe("2.0");
        published.Entries.Count.ShouldBe(1);
        published.Find("repair-boundary").ShouldBeNull();
    }

    [Fact(DisplayName = "Given a draft that adds a new kind, when ValidateAsync runs, then the catalog version bumps minor")]
    public async Task AddingKindBumpsMinorVersionAsync()
    {
        var verify = new NodeKindCatalogEntry("verify", VerifyDescriptor());
        var previous = Catalog("1.0", [verify]);
        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);

        var lifecycleEntry = new NodeKindCatalogEntry(
            "escalate",
            new NodeKindDescriptor(
                Key: "escalate",
                Title: "Escalate",
                Description: "Carries the run to a human decision.",
                OwnerSurface: NodeKindOwnerSurface.Decision,
                ParameterSchema: "escalate-payload-v1",
                OutcomePorts: [OutcomePort.Define("escalated", "Routed to a human.")],
                EvidenceRequirements: ["escalation-record"],
                RiskClass: "high",
                Idempotency: NodeKindIdempotency.Inherent,
                ApprovalFloor: 0,
                EditionsFeatureKey: null));

        var published = await validator.ValidateAsync(
            sourceRef: "git:jkl012",
            draftEntries: [verify, lifecycleEntry],
            previous: previous,
            TestContext.Current.CancellationToken);

        published.Version.ShouldBe("1.1");
        published.Entries.Count.ShouldBe(2);
        published.Find("escalate").ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a draft that alters an existing kind's ports, when ValidateAsync runs, then the catalog version bumps major")]
    public async Task AlteringPortsBumpsMajorVersionAsync()
    {
        var verify = new NodeKindCatalogEntry("verify", VerifyDescriptor());
        var previous = Catalog("1.1", [verify]);

        // Same kind, but the verify node now declares an additional port.
        var alteredVerify = new NodeKindCatalogEntry(
            "verify",
            VerifyDescriptor(ports: [
                OutcomePort.Define("passed", "Verifier ran and the artifact passed."),
                OutcomePort.Define("failed", "Verifier ran and the artifact failed."),
                OutcomePort.Define("inconclusive", "Verifier ran but produced no verdict."),
                OutcomePort.Define("infrastructure-error", "Verifier never ran."),
                OutcomePort.Define("re-run", "A new port breaks the contract."),
            ]));

        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);

        var published = await validator.ValidateAsync(
            sourceRef: "git:mno345",
            draftEntries: [alteredVerify],
            previous: previous,
            TestContext.Current.CancellationToken);

        // ALTERED → bump major regardless of the previous minor number.
        published.Version.ShouldBe("2.0");
        published.Entries.Count.ShouldBe(1);
        published.Find("verify").ShouldNotBeNull()
            .Descriptor.OutcomePorts.Count.ShouldBe(5);
    }

    [Fact(DisplayName = "Given a draft identical to the previous catalog, when ValidateAsync runs, then the version is unchanged")]
    public async Task UnchangedDraftKeepsVersionAsync()
    {
        var verify = new NodeKindCatalogEntry("verify", VerifyDescriptor());
        var previous = Catalog("1.4", [verify]);
        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);

        var published = await validator.ValidateAsync(
            sourceRef: "git:pqr678",
            draftEntries: [verify],
            previous: previous,
            TestContext.Current.CancellationToken);

        published.Version.ShouldBe("1.4");
    }

    [Fact(DisplayName = "Given a held catalog pin to v1, when a new v2 catalog is published, then the v1 reference keeps its original entries (immutability)")]
    public async Task PinnedCatalogStaysImmutableAfterNewVersionAsync()
    {
        var verify = new NodeKindCatalogEntry("verify", VerifyDescriptor());
        var v1 = Catalog("1.0", [verify]);
        var previousEntries = v1.Entries.ToList();

        var lifecycleEntry = new NodeKindCatalogEntry(
            "escalate",
            new NodeKindDescriptor(
                Key: "escalate",
                Title: "Escalate",
                Description: "Carries the run to a human decision.",
                OwnerSurface: NodeKindOwnerSurface.Decision,
                ParameterSchema: "escalate-payload-v1",
                OutcomePorts: [OutcomePort.Define("escalated", "Routed to a human.")],
                EvidenceRequirements: ["escalation-record"],
                RiskClass: "high",
                Idempotency: NodeKindIdempotency.Inherent,
                ApprovalFloor: 0,
                EditionsFeatureKey: null));

        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);
        var v2 = await validator.ValidateAsync(
            sourceRef: "git:stu901",
            draftEntries: [verify, lifecycleEntry],
            previous: v1,
            TestContext.Current.CancellationToken);

        v2.Version.ShouldBe("1.1");

        // The v1 pin a run is executing against must still resolve to its
        // original entries — a publication never mutates an existing
        // version. Spec scenario: "Republished procedure keeps its
        // catalog pin".
        v1.Version.ShouldBe("1.0");
        v1.Entries.ShouldBe(previousEntries);
        v1.Find("escalate").ShouldBeNull();

        v2.Version.ShouldBe("1.1");
        v2.Find("escalate").ShouldNotBeNull();
        v2.Entries.ShouldNotBe(v1.Entries);
    }

    [Fact(DisplayName = "Given a draft entry with an empty key, when ValidateAsync runs, then schema validation refuses the catalog")]
    public async Task SchemaRejectsEmptyKeyAsync()
    {
        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);

        var broken = new NodeKindCatalogEntry(
            string.Empty,
            VerifyDescriptor());

        var exception = await Should.ThrowAsync<ProcedureNodeKindsDomainException>(
            () => validator.ValidateAsync(
                sourceRef: "test",
                draftEntries: [broken],
                previous: null,
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureNodeKindsDomainException.SchemaViolation);
    }

    [Fact(DisplayName = "Given a draft entry with a duplicate port name, when ValidateAsync runs, then schema validation refuses the catalog")]
    public async Task SchemaRejectsDuplicatePortsAsync()
    {
        var verify = new NodeKindCatalogEntry(
            "verify",
            VerifyDescriptor(ports: [
                OutcomePort.Define("passed", "First."),
                OutcomePort.Define("passed", "Duplicate — must be refused."),
            ]));

        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);

        var exception = await Should.ThrowAsync<ProcedureNodeKindsDomainException>(
            () => validator.ValidateAsync(
                sourceRef: "test",
                draftEntries: [verify],
                previous: null,
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureNodeKindsDomainException.SchemaViolation);
        exception.Message.ShouldContain("passed");
    }

    [Fact(DisplayName = "Given a draft entry whose outcome ports contain an Unspecified placeholder, when ValidateAsync runs, then schema validation refuses the catalog")]
    public async Task SchemaRejectsUnspecifiedPortAsync()
    {
        // OutcomePort.Define rejects blank names by construction (the
        // smart-type guards the invariant), but the validator's schema
        // check is defense-in-depth: a descriptor carrying the
        // <unspecified> placeholder still reaches the validator and must
        // be refused as a schema violation rather than silently flowing
        // through.
        var verify = new NodeKindCatalogEntry(
            "verify",
            VerifyDescriptor(ports: [OutcomePort.Unspecified]));

        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);

        var exception = await Should.ThrowAsync<ProcedureNodeKindsDomainException>(
            () => validator.ValidateAsync(
                sourceRef: "test",
                draftEntries: [verify],
                previous: null,
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureNodeKindsDomainException.SchemaViolation);
    }

    [Fact(DisplayName = "Given an empty draft, when ValidateAsync runs, then schema validation refuses with a schema violation")]
    public async Task SchemaRejectsEmptyDraftAsync()
    {
        var lookup = Substitute.For<IProcedureKindUsageLookup>();
        var validator = new CatalogValidator(lookup);

        var exception = await Should.ThrowAsync<ProcedureNodeKindsDomainException>(
            () => validator.ValidateAsync(
                sourceRef: "test",
                draftEntries: [],
                previous: null,
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureNodeKindsDomainException.SchemaViolation);
    }
}
