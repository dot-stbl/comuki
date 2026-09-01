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
/// Digest assembly (add-chat-memory variant Z): lexical relevance against
/// the task text, the 5 freshest standing facts deduplicated against the
/// relevant list, and the empty-memory degenerate case.
/// </summary>
public sealed class MemoryDigestShould
{
    private static readonly DateTimeOffset baseTime = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given facts overlapping the task, when BuildAsync is called, then overlap outranks freshness")]
    public async Task RankRelevantByTaskOverlapAsync()
    {
        var unrelatedFresh = Fact("css-prefs", "the user prefers dark themes", MemoryFactKind.Standing, baseTime);
        var relatedOld = Fact("deploy-prefs", "deploys go through docker compose", MemoryFactKind.Standing, baseTime.AddDays(-5));
        var relatedNew = Fact("release-flow", "releases always use docker compose and a migrate step", MemoryFactKind.Standing, baseTime);
        var store = new FakeMemoryStore([unrelatedFresh, relatedOld, relatedNew]);
        var digest = new MemoryDigest(store);

        var result = await digest.BuildAsync("docker compose deploy for the release", MemoryScope.User, "user-1", TestContext.Current.CancellationToken);

        result.Relevant.First().TopicKey.ShouldBeOneOf("release-flow", "deploy-prefs");
        result.Relevant.Last().TopicKey.ShouldBe("css-prefs");
    }

    [Fact(DisplayName = "Given more standing facts than the relevant limit, when the freshest standing are built, then they exclude the relevant ones")]
    public async Task DeduplicateFreshestAgainstRelevantAsync()
    {
        // topic-6 is the OLDEST but the only lexical match: relevant keeps
        // it plus the four freshest; the fifth-freshest (topic-4) is left
        // over for the freshest-standing list
        var facts = Enumerable.Range(0, 7)
            .Select(static offset => Fact(
                $"topic-{offset}",
                offset == 6 ? "special zero words" : "plain filler note",
                MemoryFactKind.Standing,
                baseTime.AddDays(-offset)))
            .ToArray();
        var store = new FakeMemoryStore(facts);
        var digest = new MemoryDigest(store);

        var result = await digest.BuildAsync("special zero words", MemoryScope.User, "user-1", TestContext.Current.CancellationToken);

        result.Relevant.Count.ShouldBe(MemoryFactQuery.DigestRelevantLimit);
        result.Relevant.Select(static entry => entry.TopicKey).ShouldContain("topic-6");
        result.FreshestStanding.Select(static entry => entry.TopicKey).ShouldBe(["topic-4"]);
        result.Relevant.Concat(result.FreshestStanding).Select(static entry => entry.TopicKey).Distinct().Count().ShouldBe(
            result.Relevant.Count + result.FreshestStanding.Count);
    }

    [Fact(DisplayName = "Given an empty memory, when BuildAsync is called, then the digest is empty")]
    public async Task ReturnEmptyDigestForEmptyMemoryAsync()
    {
        var digest = new MemoryDigest(new FakeMemoryStore([]));

        var result = await digest.BuildAsync("anything", MemoryScope.Global, "global", TestContext.Current.CancellationToken);

        result.Relevant.ShouldBeEmpty();
        result.FreshestStanding.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given digest entries, then each carries topic, text and kind key")]
    public async Task CarryKindKeysOnEntriesAsync()
    {
        var standing = Fact("topic-a", "text a", MemoryFactKind.Standing, baseTime);
        var digest = new MemoryDigest(new FakeMemoryStore([standing]));

        var result = await digest.BuildAsync("text a", MemoryScope.User, "user-1", TestContext.Current.CancellationToken);

        result.Relevant.ShouldHaveSingleItem().Kind.ShouldBe("standing");
        result.Relevant.ShouldHaveSingleItem().Text.ShouldBe("text a");
    }

    private static MemoryFactView Fact(string topicKey, string text, MemoryFactKind kind, DateTimeOffset createdAt)
    {
        return new MemoryFactView(
            MemoryFactId.New(),
            MemoryScope.User,
            "user-1",
            kind,
            topicKey,
            text,
            MemorySource.Chat,
            "user-1",
            createdAt);
    }

    /// <summary>
    /// In-memory IMemoryStore: search answers with the fallback-ordered
    /// visible facts of the query shape; write/forget/sweep are unused by
    /// the digest tests.
    /// </summary>
    private sealed class FakeMemoryStore(IReadOnlyList<MemoryFactView> facts) : IMemoryStore
    {
        public Task<MemoryFactView> WriteAsync(MemoryFactWrite write, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("write is out of scope for the digest fake");
        }

        public Task<IReadOnlyList<MemoryFactView>> SearchAsync(MemoryFactQuery query, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MemoryFactView> results = [.. facts
                .Where(fact => query.Scope is null || fact.Scope == query.Scope)
                .Where(fact => query.SubjectId is null || fact.SubjectId == query.SubjectId)
                .Where(fact => query.Kind is null || fact.Kind == query.Kind)
                .OrderByDescending(static fact => fact.Kind == MemoryFactKind.Standing)
                .ThenByDescending(static fact => fact.CreatedAt)
                .Take(query.Limit)];

            return Task.FromResult(results);
        }

        public Task<IReadOnlyList<MemoryFactView>> ListAsync(MemoryScope scope, string subjectId, CancellationToken cancellationToken = default)
        {
            return SearchAsync(new MemoryFactQuery(Scope: scope, SubjectId: subjectId), cancellationToken);
        }

        public Task<bool> ForgetAsync(MemoryFactId id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("forget is out of scope for the digest fake");
        }

        public Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("sweep is out of scope for the digest fake");
        }
    }
}
