using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Application.Views;

/// <summary>
/// Read-facing projection of a memory fact. Wire/stored keys use the
/// kebab-case strings from the Keys classes; ids are UUIDv7 strings.
/// The embedding vector is deliberately absent — it never leaves the store.
/// </summary>
/// <param name="Id">Fact id (UUIDv7 string).</param>
/// <param name="Scope">Scope key: user | project | global.</param>
/// <param name="SubjectId">Owner id inside the scope.</param>
/// <param name="Kind">Kind key: standing | ephemeral.</param>
/// <param name="TopicKey">Canonicalized topic key.</param>
/// <param name="Text">The fact text.</param>
/// <param name="Source">Source key: chat | human | run | learning-approved.</param>
/// <param name="CreatedBy">Who wrote the fact.</param>
/// <param name="CreatedAt">When the fact was written.</param>
/// <param name="LexicalRank">
/// <c>ts_rank</c> score from the lexical (FTS) path of the store;
/// zero when the FTS column is missing, the query was empty, or the
/// cosine path was the only ranker that produced this row.
/// </param>
/// <param name="VectorRank">
/// 1 − cosine distance from the vector (pgvector) path of the store;
/// zero when the embedding column is missing, the search had no embedding,
/// or the lexical path was the only ranker that produced this row.
/// </param>
/// <param name="FusedScore">
/// Reciprocal Rank Fusion of the two lists with k=60 and per-list weight
/// 1.0 (default — see <see cref="Ranking.MemoryHybridRanking"/>).
/// Ordering on read is by this score descending.
/// </param>
public sealed record MemoryFactView(
    MemoryFactId Id,
    MemoryScope Scope,
    string SubjectId,
    MemoryFactKind Kind,
    string TopicKey,
    string Text,
    MemorySource Source,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    float LexicalRank = 0f,
    float VectorRank = 0f,
    float FusedScore = 0f);
