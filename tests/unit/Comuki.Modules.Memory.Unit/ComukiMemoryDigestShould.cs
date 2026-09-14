using Comuki.Modules.Memory.Application.Digest;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// The IMemoryDigest contract adapter: scope/subject mapping from the
/// wire-shaped request onto the module's scope model, the prompt text
/// rendering, and the empty-memory degenerate case (empty string — the
/// turn service's "do not journal" signal).
/// </summary>
public sealed class ComukiMemoryDigestShould
{
    private static readonly DateTimeOffset baseTime = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a project-scoped request, when BuildDigestAsync is called, then the store is queried with the project scope and subject")]
    public async Task MapProjectScopeOntoTheStoreQueryAsync()
    {
        var subjectId = Guid.NewGuid();
        var store = new RecordingMemoryStore([]);
        var digest = new ComukiMemoryDigest(new MemoryDigest(store));

        await digest.BuildDigestAsync(
            new Shared.Contracts.Memory.MemoryDigestRequest("project", subjectId, "the task"),
            TestContext.Current.CancellationToken);

        var query = store.Queries.First();
        query.Scope.ShouldBe(MemoryScope.Project);
        query.SubjectId.ShouldBe(subjectId.ToString());
    }

    [Fact(DisplayName = "Given a user-scoped request, when BuildDigestAsync is called, then the store is queried with the user scope and subject")]
    public async Task MapUserScopeOntoTheStoreQueryAsync()
    {
        var subjectId = Guid.NewGuid();
        var store = new RecordingMemoryStore([]);
        var digest = new ComukiMemoryDigest(new MemoryDigest(store));

        await digest.BuildDigestAsync(
            new Shared.Contracts.Memory.MemoryDigestRequest("user", subjectId, "the task"),
            TestContext.Current.CancellationToken);

        var query = store.Queries.First();
        query.Scope.ShouldBe(MemoryScope.User);
        query.SubjectId.ShouldBe(subjectId.ToString());
    }

    [Fact(DisplayName = "Given a global request, when BuildDigestAsync is called, then the well-known global subject is queried, not the request id")]
    public async Task MapGlobalScopeOntoTheGlobalSubjectAsync()
    {
        var store = new RecordingMemoryStore([]);
        var digest = new ComukiMemoryDigest(new MemoryDigest(store));

        await digest.BuildDigestAsync(
            new Shared.Contracts.Memory.MemoryDigestRequest("global", Guid.NewGuid(), "the task"),
            TestContext.Current.CancellationToken);

        var query = store.Queries.First();
        query.Scope.ShouldBe(MemoryScope.Global);
        query.SubjectId.ShouldBe(MemoryScopeKeys.GlobalSubject);
    }

    [Fact(DisplayName = "Given an unknown scope kind, when BuildDigestAsync is called, then the adapter refuses instead of guessing")]
    public async Task RefuseUnknownScopeKindAsync()
    {
        var digest = new ComukiMemoryDigest(new MemoryDigest(new RecordingMemoryStore([])));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => digest.BuildDigestAsync(
                new Shared.Contracts.Memory.MemoryDigestRequest("tenant", Guid.NewGuid(), "the task"),
                TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given digest facts, when BuildDigestAsync is called, then relevant entries render as kind-prefixed lines under their heading")]
    public async Task RenderRelevantEntriesWithKindPrefixesAsync()
    {
        var subjectId = Guid.NewGuid();
        var standing = Fact(subjectId, "deploy", "deploys use docker compose", MemoryFactKind.Standing, baseTime);
        var note = Fact(subjectId, "deploy-note", "docker compose step is pending", MemoryFactKind.Ephemeral, baseTime.AddMinutes(1));
        var store = new RecordingMemoryStore([standing, note]);
        var digest = new ComukiMemoryDigest(new MemoryDigest(store));

        var text = await digest.BuildDigestAsync(
            new Shared.Contracts.Memory.MemoryDigestRequest("user", subjectId, "docker compose"),
            TestContext.Current.CancellationToken);

        text.ShouldStartWith("Relevant memory");
        text.ShouldContain("- [standing] deploy: deploys use docker compose");
        text.ShouldContain("- [ephemeral] deploy-note: docker compose step is pending");
        // every fact is relevant here — the freshest-standing section stays away
        text.ShouldNotContain("Standing memory");
    }

    [Fact(DisplayName = "Given more standing facts than the relevant cap, when BuildDigestAsync is called, then the overflow renders under the standing heading")]
    public async Task RenderFreshestStandingOverflowAsync()
    {
        var subjectId = Guid.NewGuid();
        // the oldest fact is the only lexical match; relevant keeps it plus
        // the four freshest; the fifth-freshest is left for the standing section
        var facts = Enumerable.Range(0, 7)
            .Select(offset => Fact(
                subjectId,
                $"topic-{offset}",
                offset == 6 ? "special docker compose words" : "plain filler note",
                MemoryFactKind.Standing,
                baseTime.AddMinutes(-offset)))
            .ToArray();
        var store = new RecordingMemoryStore(facts);
        var digest = new ComukiMemoryDigest(new MemoryDigest(store));

        var text = await digest.BuildDigestAsync(
            new Shared.Contracts.Memory.MemoryDigestRequest("user", subjectId, "docker compose"),
            TestContext.Current.CancellationToken);

        text.ShouldContain("Relevant memory");
        text.ShouldContain("Standing memory");
        text.ShouldContain("- [standing] topic-4: plain filler note");
        // topic-4 is deduplicated: it shows once, in the standing section
        (text.Split(["- [standing] topic-4:"], StringSplitOptions.None).Length - 1).ShouldBe(1);
        text.ShouldContain("- [standing] topic-6: special docker compose words");
    }

    [Fact(DisplayName = "Given an empty memory, when BuildDigestAsync is called, then the digest is the empty string (the do-not-journal signal)")]
    public async Task RenderEmptyDigestAsEmptyStringAsync()
    {
        var digest = new ComukiMemoryDigest(new MemoryDigest(new RecordingMemoryStore([])));

        var text = await digest.BuildDigestAsync(
            new Shared.Contracts.Memory.MemoryDigestRequest("user", Guid.NewGuid(), "the task"),
            TestContext.Current.CancellationToken);

        text.ShouldBe(string.Empty);
    }

    private static MemoryFactView Fact(Guid subjectId, string topicKey, string text, MemoryFactKind kind, DateTimeOffset createdAt)
    {
        return new MemoryFactView(
            MemoryFactId.New(),
            MemoryScope.User,
            subjectId.ToString(),
            kind,
            topicKey,
            text,
            MemorySource.Chat,
            "user-1",
            createdAt);
    }

    /// <summary>
    /// In-memory IMemoryStore that records every search query and answers
    /// with fixed facts filtered by the query shape (the digest performs
    /// two searches per build — relevant, then freshest standing).
    /// </summary>
    private sealed class RecordingMemoryStore(IReadOnlyList<MemoryFactView> facts) : IMemoryStore
    {
        public List<MemoryFactQuery> Queries { get; } = [];

        public Task<MemoryFactView> WriteAsync(MemoryFactWrite write, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("write is out of scope for the digest adapter fake");
        }

        public Task<IReadOnlyList<MemoryFactView>> SearchAsync(MemoryFactQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            IReadOnlyList<MemoryFactView> results = [.. facts
                .Where(fact => query.Scope is null || fact.Scope == query.Scope)
                .Where(fact => query.SubjectId is null || fact.SubjectId == query.SubjectId)
                .Where(fact => query.Kind is null || fact.Kind == query.Kind)
                .OrderByDescending(static fact => fact.CreatedAt)
                .Take(query.Limit)];

            return Task.FromResult(results);
        }

        public Task<IReadOnlyList<MemoryFactView>> ListAsync(
            MemoryScope scope,
            string subjectId,
            int limit = IMemoryStore.DefaultListLimit,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return SearchAsync(new MemoryFactQuery(Scope: scope, SubjectId: subjectId, Limit: limit), cancellationToken);
        }

        public Task<bool> ForgetAsync(MemoryFactId id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("forget is out of scope for the digest adapter fake");
        }

        public Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("sweep is out of scope for the digest adapter fake");
        }
    }
}
