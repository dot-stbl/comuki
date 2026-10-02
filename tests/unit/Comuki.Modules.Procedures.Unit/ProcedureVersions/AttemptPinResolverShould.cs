using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.ProcedureVersions;

/// <summary>
/// Unit tests for task 3.3: the attempt-pin resolver returns the
/// most recently published version for a (project, procedureKey)
/// pair, and the in-memory ledger records the from/to transition
/// between attempts (spec scenario: "the attempt ledger records the
/// version change between attempts"). The resolver survives a
/// concurrent republish by reading through the version store's
/// content-deduplicated history (task 2.4's write-once contract).
/// </summary>
public sealed class AttemptPinResolverShould
{
    [Fact(DisplayName = "Given no published versions, when the resolver is called, then it returns null (nothing to pin)")]
    public async Task NoPublishedVersionReturnsNullAsync()
    {
        var store = Substitute.For<IProcedureVersionStore>();
        store.ListByProcedureAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var resolver = new AttemptPinResolver(store);

        var result = await resolver.ResolveCurrentVersionIdAsync(
            Guid.NewGuid(),
            "test-procedure",
            TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a single published version, when the resolver is called, then it returns that version id")]
    public async Task ReturnsLatestVersionIdAsync()
    {
        var projectId = Guid.NewGuid();
        var store = Substitute.For<IProcedureVersionStore>();
        store.ListByProcedureAsync(projectId, "test-procedure", Arg.Any<CancellationToken>())
            .Returns([NewVersion("v3")]);
        var resolver = new AttemptPinResolver(store);

        var result = await resolver.ResolveCurrentVersionIdAsync(
            projectId,
            "test-procedure",
            TestContext.Current.CancellationToken);

        result.ShouldBe("v3");
    }

    [Fact(DisplayName = "Given several published versions, when the resolver is called, then it returns the latest one (first in the descending-by-created-at list)")]
    public async Task ReturnsFirstEntryAsLatestAsync()
    {
        var projectId = Guid.NewGuid();
        var store = Substitute.For<IProcedureVersionStore>();
        store.ListByProcedureAsync(projectId, "test-procedure", Arg.Any<CancellationToken>())
            .Returns([
                NewVersion("v9"),
                NewVersion("v7"),
                NewVersion("v4"),
            ]);
        var resolver = new AttemptPinResolver(store);

        var result = await resolver.ResolveCurrentVersionIdAsync(
            projectId,
            "test-procedure",
            TestContext.Current.CancellationToken);

        result.ShouldBe("v9");
    }

    private static CompiledProcedureVersion NewVersion(string versionId)
    {
        return new CompiledProcedureVersion(
            VersionId: versionId,
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "test-procedure",
            CatalogVersion: "1.0",
            SourceRef: "refs/heads/main",
            GraphJson: "{}");
    }
}

public sealed class AttemptPinLedgerShould
{
    [Fact(DisplayName = "Given a transition for an attempt, when recorded, then a subsequent list call returns it in chronological order")]
    public async Task RecordsAndListsTransitionsAsync()
    {
        var ledger = new InMemoryAttemptPinLedger();
        var projectId = Guid.NewGuid();
        var firstAttempt = Guid.NewGuid();
        var secondAttempt = Guid.NewGuid();

        await ledger.RecordAsync(
            new AttemptVersionTransition(
                AttemptId: firstAttempt,
                ProjectId: projectId,
                ProcedureKey: "test-procedure",
                FromVersionId: null,
                ToVersionId: "v1",
                RecordedAt: DateTimeOffset.UtcNow.AddMinutes(-10)),
            TestContext.Current.CancellationToken);

        await ledger.RecordAsync(
            new AttemptVersionTransition(
                AttemptId: secondAttempt,
                ProjectId: projectId,
                ProcedureKey: "test-procedure",
                FromVersionId: "v1",
                ToVersionId: "v2",
                RecordedAt: DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        var entries = await ledger.ListAsync(projectId, "test-procedure", TestContext.Current.CancellationToken);

        entries.Count.ShouldBe(2);
        entries[0].AttemptId.ShouldBe(firstAttempt);
        entries[0].FromVersionId.ShouldBeNull();
        entries[0].ToVersionId.ShouldBe("v1");
        entries[1].AttemptId.ShouldBe(secondAttempt);
        entries[1].FromVersionId.ShouldBe("v1");
        entries[1].ToVersionId.ShouldBe("v2");
    }

    [Fact(DisplayName = "Given the same attempt id recorded twice, when listed, then only the first entry is present (the ledger is idempotent on attempt id)")]
    public async Task IdempotentOnAttemptIdAsync()
    {
        var ledger = new InMemoryAttemptPinLedger();
        var projectId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        await ledger.RecordAsync(
            new AttemptVersionTransition(
                AttemptId: attemptId,
                ProjectId: projectId,
                ProcedureKey: "test-procedure",
                FromVersionId: null,
                ToVersionId: "v1",
                RecordedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
            TestContext.Current.CancellationToken);

        await ledger.RecordAsync(
            new AttemptVersionTransition(
                AttemptId: attemptId,
                ProjectId: projectId,
                ProcedureKey: "test-procedure",
                FromVersionId: "v1",
                ToVersionId: "v9",
                RecordedAt: DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        var entries = await ledger.ListAsync(projectId, "test-procedure", TestContext.Current.CancellationToken);

        entries.Count.ShouldBe(1);
        entries[0].ToVersionId.ShouldBe("v1");
    }

    [Fact(DisplayName = "Given transitions for two different procedures, when listed, then only the requested procedure's transitions are returned")]
    public async Task ListsAreScopedToProcedureAsync()
    {
        var ledger = new InMemoryAttemptPinLedger();
        var projectId = Guid.NewGuid();

        await ledger.RecordAsync(
            new AttemptVersionTransition(Guid.NewGuid(), projectId, "checkout", null, "v1", DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);
        await ledger.RecordAsync(
            new AttemptVersionTransition(Guid.NewGuid(), projectId, "hotfix", null, "v1", DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        var entries = await ledger.ListAsync(projectId, "checkout", TestContext.Current.CancellationToken);

        entries.Count.ShouldBe(1);
        entries[0].ProcedureKey.ShouldBe("checkout");
    }
}
