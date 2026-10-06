using Comuki.Engine.Orchestration.Domain.Verification;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Verification;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.DbContext;

/// <summary>
/// Round-trip for <c>verifications.evidence_refs</c> through EF's
/// <c>ValueConverter</c> for the jsonb column (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "Per-gate uniqueness").
/// The converter path is what the production <c>INSERT ... ON
/// CONFLICT</c> skips (the upsert serializes the array itself), but
/// the materialiser <em>does</em> run the converter on every
/// <c>SELECT</c>. Without the smart-type
/// <see cref="GateEvidenceKindJsonConverter"/> plugged in, a freshly
/// written kind collapses to <see cref="GateEvidenceKind.Other"/>
/// ("other") on the EF read side — the kind that was just stamped
/// silently reverts on the next page load. These tests pin both
/// ends of the contract: the round-trip preserves the named kind
/// (<c>"cmdiff"</c> / <c>"stdout"</c> / <c>"stderr"</c> / <c>"json"</c>).
/// The InMemory provider materialises the value converter on every
/// read; we use a single context with <c>AsNoTracking</c> for the
/// read so the round-trip exercises the converter, not the change
/// tracker's reference-stable shortcut.
/// </summary>
public sealed class VerificationRecordEvidenceRefsConverterShould
{
    private static readonly DateTimeOffset now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a record with evidence_refs of kind cmdiff, when EF saves and reloads, then the kind round-trips as cmdiff and not the default other")]
    public async Task RoundTripPreservesNamedKindAsync()
    {
        var context = NewContext();
        var workItemId = Guid.CreateVersion7();
        var cmdiffUri = new Uri("comuki://artifacts/changeset.diff");
        var written = VerificationRecord.FromEvaluation(
            workItemId,
            "verify:generic-command-run",
            new GateVerdictResult(
                GateVerdict.Passed,
                [new GateEvidenceRef(GateEvidenceKind.Cmdiff, cmdiffUri)],
                "verifier-7"),
            now);

        context.Verifications.Add(written);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        // AsNoTracking + a cleared change tracker forces a fresh
        // materialisation through the value converter — the same
        // shape the production SELECT takes.
        var read = await context.Verifications
            .AsNoTracking()
            .SingleAsync(record => record.Id == written.Id, TestContext.Current.CancellationToken);

        read.EvidenceRefs.ShouldHaveSingleItem();
        read.EvidenceRefs[0].Kind.Value.ShouldBe("cmdiff");
        read.EvidenceRefs[0].Kind.ShouldNotBe(GateEvidenceKind.Other);
        read.EvidenceRefs[0].Uri.ShouldBe(cmdiffUri);
    }

    [Fact(DisplayName = "Given a record with every named kind, when EF saves and reloads, then each kind round-trips to its wire value")]
    public async Task RoundTripPreservesEveryNamedKindAsync()
    {
        var context = NewContext();
        var workItemId = Guid.CreateVersion7();
        var evidence = new GateEvidenceRef[]
        {
            new(GateEvidenceKind.Cmdiff, new Uri("comuki://artifacts/c.diff")),
            new(GateEvidenceKind.Stdout, new Uri("comuki://artifacts/stdout")),
            new(GateEvidenceKind.Stderr, new Uri("comuki://artifacts/stderr")),
            new(GateEvidenceKind.Json, new Uri("comuki://artifacts/result.json")),
        };
        var written = VerificationRecord.FromEvaluation(
            workItemId,
            "verify:generic-command-run",
            new GateVerdictResult(GateVerdict.Passed, evidence, "verifier-7"),
            now);

        context.Verifications.Add(written);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var read = await context.Verifications
            .AsNoTracking()
            .SingleAsync(record => record.Id == written.Id, TestContext.Current.CancellationToken);

        read.EvidenceRefs.Count.ShouldBe(4);
        read.EvidenceRefs.Select(static evidence => evidence.Kind).ShouldBe(
            [GateEvidenceKind.Cmdiff, GateEvidenceKind.Stdout, GateEvidenceKind.Stderr, GateEvidenceKind.Json],
            ignoreOrder: false);
        read.EvidenceRefs.Select(static evidence => evidence.Kind.Value).ShouldBe(
            ["cmdiff", "stdout", "stderr", "json"],
            ignoreOrder: false);
    }

    private static OrchestrationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"orch-ctx-{Guid.NewGuid():N}")
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor: null);
    }
}
