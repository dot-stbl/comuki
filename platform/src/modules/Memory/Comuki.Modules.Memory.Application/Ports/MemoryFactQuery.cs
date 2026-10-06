using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;

namespace Comuki.Modules.Memory.Application.Ports;

/// <summary>
/// Search parameters. Superseded and expired facts are always excluded.
/// With <see cref="Embedding"/> set and pgvector available the store ranks
/// by cosine distance; with <see cref="Text"/> set and the FTS column
/// available the store ranks by <c>ts_rank</c>. When both are present
/// the results are fused via RRF (k=60) — see the memory module's
/// hybrid ranking. Neither — and always as a safety net — falls back to
/// the scope+kind+freshest ranking (memory must work without embeddings
/// or FTS per the add-chat-memory contract).
/// </summary>
/// <param name="Scope">Restrict to one scope; null searches all scopes.</param>
/// <param name="SubjectId">Restrict to one subject; null searches all subjects.</param>
/// <param name="Kind">Restrict to one fact kind; null searches both.</param>
/// <param name="Embedding">Optional query vector for the cosine path.</param>
/// <param name="Text">
/// Optional user query text for the lexical path (<c>plainto_tsquery</c>
/// input). Empty by default — callers that already supply an embedding
/// typically pass the same text they embedded.
/// </param>
/// <param name="MissionId">
/// Optional mission id, only meaningful when <paramref name="Scope" />
/// is <see cref="MemoryScope.Mission" /> (or null with at least one
/// mission-scoped fact expected to match). When the call site passes
/// a non-null <c>MissionId</c>, the query filter includes
/// <c>scope = 'mission' AND subject_id = MissionId</c>. When the call site
/// leaves it null, mission-scoped rows are excluded — the filter is
/// fail-closed so an unrestricted caller cannot read another mission's
/// facts.
/// </param>
/// <param name="Limit">Maximum facts returned.</param>
public sealed record MemoryFactQuery(
    MemoryScope? Scope = null,
    string? SubjectId = null,
    MemoryFactKind? Kind = null,
    float[]? Embedding = null,
    string? Text = null,
    Guid? MissionId = null,
    int Limit = 10)
{
    /// <summary>Digest default: top-5 relevant facts.</summary>
    public const int DigestRelevantLimit = 5;

    /// <summary>Digest default: 5 freshest standing facts.</summary>
    public const int DigestFreshestLimit = 5;

    /// <summary>Digest default: how many fallback-ranked candidates the lexical scoring sees.</summary>
    public const int DigestCandidateLimit = 25;
}
