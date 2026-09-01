using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Ranking;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;

namespace Comuki.Modules.Memory.Application.Digest;

/// <summary>
/// The single shared brain-context assembler (add-chat-memory variant Z):
/// called by BOTH brain callers — the chat graph and orchestration
/// auto-replan. Builds a compact digest (top-5 relevant + 5 freshest
/// standing, deduplicated) from the memory store. Relevance today is
/// lexical overlap between the task text and the fact — a deterministic
/// stand-in for the cosine path that activates once callers pass query
/// embeddings into the store; ties break standing-first, freshest.
/// </summary>
/// <param name="store"></param>
public sealed class MemoryDigest(IMemoryStore store)
{
    private static readonly char[] tokenSeparators =
        [' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_', '*', '`', '#', '>', '<', '|', '=', '+'];

    /// <summary>
    /// Builds the digest for one task: the top relevant facts of the
    /// scope+subject scored by lexical overlap with
    /// <paramref name="taskText"/>, plus the freshest standing facts not
    /// already in the relevant list.
    /// </summary>
    /// <param name="taskText">The task being assembled context for.</param>
    /// <param name="scope">Whose memory to read.</param>
    /// <param name="subjectId">Owner id inside the scope.</param>
    /// <param name="cancellationToken"></param>
    public async Task<MemoryDigestResult> BuildAsync(
        string taskText,
        MemoryScope scope,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var taskTokens = TokensOf(taskText);

        var candidates = await store.SearchAsync(
            new MemoryFactQuery(Scope: scope, SubjectId: subjectId, Limit: MemoryFactQuery.DigestCandidateLimit),
            cancellationToken);

        var relevant = candidates
            .Select(fact => (Fact: fact, Score: ScoreOf(fact)))
            .OrderByDescending(static entry => entry.Score)
            .ThenByDescending(static entry => entry.Fact.Kind == MemoryFactKind.Standing)
            .ThenByDescending(static entry => entry.Fact.CreatedAt)
            .Take(MemoryFactQuery.DigestRelevantLimit)
            .Select(static entry => entry.Fact)
            .ToArray();

        var relevantIds = relevant.Select(static fact => fact.Id).ToHashSet();
        var freshest = await store.SearchAsync(
            new MemoryFactQuery(
                Scope: scope,
                SubjectId: subjectId,
                Kind: MemoryFactKind.Standing,
                Limit: MemoryFactQuery.DigestFreshestLimit),
            cancellationToken);

        var freshestStanding = MemoryFallbackRanking.Rank(freshest, MemoryFactQuery.DigestFreshestLimit)
            .Where(fact => !relevantIds.Contains(fact.Id))
            .ToArray();

        return new MemoryDigestResult(
            [.. relevant.Select(AsEntry)],
            [.. freshestStanding.Select(AsEntry)]);

        int ScoreOf(MemoryFactView fact) => taskTokens.Count == 0 ? 0 : TokensOf($"{fact.TopicKey} {fact.Text}").Count(taskTokens.Contains);

        static HashSet<string> TokensOf(string text)
        {
            return [.. text
                .ToLowerInvariant()
                .Split(tokenSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Where(static token => token.Length >= 3)];
        }

        static MemoryDigestEntry AsEntry(MemoryFactView fact) => new(fact.TopicKey, fact.Text, MemoryFactKindKeys.Key(fact.Kind));
    }
}
