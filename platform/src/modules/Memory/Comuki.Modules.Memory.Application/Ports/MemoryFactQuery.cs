using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;

namespace Comuki.Modules.Memory.Application.Ports;

/// <summary>
/// Search parameters. Superseded and expired facts are always excluded.
/// With <see cref="Embedding"/> set and pgvector available the store ranks
/// by cosine distance; otherwise — and always as a safety net — the
/// scope+kind+freshest fallback ranking applies (memory must work without
/// embeddings per the add-chat-memory contract).
/// </summary>
/// <param name="Scope">Restrict to one scope; null searches all scopes.</param>
/// <param name="SubjectId">Restrict to one subject; null searches all subjects.</param>
/// <param name="Kind">Restrict to one fact kind; null searches both.</param>
/// <param name="Embedding">Optional query vector for the cosine path.</param>
/// <param name="Limit">Maximum facts returned.</param>
public sealed record MemoryFactQuery(
    MemoryScope? Scope = null,
    string? SubjectId = null,
    MemoryFactKind? Kind = null,
    float[]? Embedding = null,
    int Limit = 10)
{
    /// <summary>Digest default: top-5 relevant facts.</summary>
    public const int DigestRelevantLimit = 5;

    /// <summary>Digest default: 5 freshest standing facts.</summary>
    public const int DigestFreshestLimit = 5;

    /// <summary>Digest default: how many fallback-ranked candidates the lexical scoring sees.</summary>
    public const int DigestCandidateLimit = 25;
}
