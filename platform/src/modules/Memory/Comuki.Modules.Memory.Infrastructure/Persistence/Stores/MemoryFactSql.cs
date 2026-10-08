using System.Data;
using System.Globalization;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Shared.Kernel;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// Raw-SQL surface for the pgvector <c>embedding</c> column: literal
/// formatting, the availability probe, the embedding UPDATE and the
/// cosine-distance SELECT. The column lives outside the EF model on
/// purpose — no EF-pgvector provider, no vector materialized in .NET.
/// All SQL references the per-module <see cref="MemoryDatabase.Schema"/>
/// so the queries find the table regardless of <c>search_path</c>.
/// </summary>
public static class MemoryFactSql
{
    /// <summary>Availability probe: does the embedding column exist (pgvector was present at migration time)?</summary>
    public const string EmbeddingColumnExistsSql =
        "SELECT EXISTS (SELECT 1 FROM information_schema.columns "
        + "WHERE table_schema = '" + MemoryDatabase.Schema + "' "
        + "AND table_name = '" + MemoryDatabase.MemoryFacts + "' AND column_name = 'embedding')";

    /// <summary>
    /// Availability probe: does the <c>text_tsv</c> generated column exist
    /// (the tsvector + GIN migration was applied)? When false the hybrid
    /// retrieval falls back to vector-only ranking — the lexical rank
    /// defaults to zero rather than raising.
    /// </summary>
    public const string LexicalColumnExistsSql =
        "SELECT EXISTS (SELECT 1 FROM information_schema.columns "
        + "WHERE table_schema = '" + MemoryDatabase.Schema + "' "
        + "AND table_name = '" + MemoryDatabase.MemoryFacts + "' AND column_name = 'text_tsv')";

    /// <summary>
    /// Computes the lexical rank for one row as a single-column SELECT that
    /// fits the same <c>SELECT id, scope, subject_id, …</c> shape the
    /// cosine path uses, so the hybrid path can attach the rank without a
    /// schema change. The score is the Postgres <c>ts_rank</c> of the
    /// generated <c>text_tsv</c> column against the query's
    /// <c>plainto_tsquery('simple', @lexicalQuery)</c> — <c>plainto_tsquery</c>
    /// is the safe choice (treats input as plain text, no operator
    /// parsing, no lexeme injection). When the column is missing the
    /// caller probes first and substitutes zero rather than running this
    /// query (the table has no <c>text_tsv</c> at all on a fresh install
    /// that never applied the FTS migration).
    /// </summary>
    public const string LexicalRankSql =
        "SELECT id, scope, subject_id, kind, topic_key, text, source, created_by, created_at, "
        + "       ts_rank(text_tsv, plainto_tsquery('simple', @lexicalQuery)) AS lexical_rank "
        + "FROM " + MemoryDatabase.Schema + "." + MemoryDatabase.MemoryFacts + " "
        + "WHERE superseded_at IS NULL "
        + "  AND text_tsv @@ plainto_tsquery('simple', @lexicalQuery) "
        + "  AND (@unrestricted OR scope = 'global' OR (scope = 'project' AND subject_id = ANY(@allowedProjectSubjectKeys::text[]))) "
        + "  AND (@scope IS NULL OR scope = @scope) "
        + "  AND (@subject IS NULL OR subject_id = @subject) "
        + "  AND (@kind IS NULL OR kind = @kind) "
        + "ORDER BY lexical_rank DESC, created_at DESC "
        + "LIMIT @limit";

    /// <summary>
    /// Escapes a user-supplied query text for use as a <c>plainto_tsquery</c>
    /// argument. <c>plainto_tsquery</c> is itself safe (no operator parsing)
    /// but stripping surrounding quotes prevents the rare case where a
    /// query starts with <c>'</c> or <c>"</c> and Postgres rejects the
    /// unterminated literal. Returns an empty string when the query is
    /// blank after trim — an empty tsquery matches nothing, so the
    /// caller treats it as a no-result search. The matched-pair stripping
    /// reuses <see cref="YamlishFrontmatter.StripQuotes"/> so the rule
    /// lives in one place rather than being a hand-rolled mirror.
    /// </summary>
    /// <param name="query">User query text.</param>
    public static string LexicalQuery(string query)
    {
        var trimmed = query.Trim();
        return trimmed.Length == 0 ? string.Empty : YamlishFrontmatter.StripQuotes(trimmed);
    }

    /// <summary>Writes the embedding of one fact row (inside the write transaction).</summary>
    public const string UpdateEmbeddingSql =
        "UPDATE " + MemoryDatabase.Schema + "." + MemoryDatabase.MemoryFacts
        + " SET embedding = @vector::vector WHERE id = @id";

    /// <summary>
    /// Cosine-distance search over embedded, visible facts. This table has
    /// no project id column to scope by — the object axis here is
    /// <c>scope</c>/<c>subject_id</c> (see <c>MemoryDbContext</c>'s
    /// <c>HasQueryFilter</c> on <c>MemoryFact</c>, which this raw SQL
    /// query reproduces because it runs outside EF's model and that
    /// filter cannot reach it): <c>@unrestricted</c> or a global row
    /// widens; a project row must have its subject id in
    /// <c>@allowedProjectSubjectKeys</c>; a user row is never matched
    /// unless <c>@unrestricted</c> — there is no per-user identity axis to
    /// check it against. <c>@allowedProjectSubjectKeys</c> must always be
    /// a real (possibly empty) array, never NULL (<c>= ANY(NULL)</c> is
    /// NULL, not true). The narrowing filter parameters (<c>@scope</c> /
    /// <c>@subject</c> / <c>@kind</c>) widen on NULL and must arrive
    /// text-typed — an untyped NULL parameter fails with 42P08. The
    /// vector parameter carries an untyped literal typed by the explicit
    /// <c>::vector</c> cast.
    /// </summary>
    public const string CosineSearchSql =
        "SELECT id, scope, subject_id, kind, topic_key, text, source, created_by, created_at, "
        + "       (1 - (embedding <=> @vector::vector)) AS vector_rank "
        + "FROM " + MemoryDatabase.Schema + "." + MemoryDatabase.MemoryFacts + " "
        + "WHERE superseded_at IS NULL "
        + "  AND (kind <> 'ephemeral' OR created_at >= @cutoff) "
        + "  AND (@unrestricted OR scope = 'global' OR (scope = 'project' AND subject_id = ANY(@allowedProjectSubjectKeys::text[]))) "
        + "  AND (@scope IS NULL OR scope = @scope) "
        + "  AND (@subject IS NULL OR subject_id = @subject) "
        + "  AND (@kind IS NULL OR kind = @kind) "
        + "  AND embedding IS NOT NULL "
        + "ORDER BY embedding <=> @vector::vector "
        + "LIMIT @limit";

    /// <summary>Formats a vector as a pgvector literal (<c>[1,0.5,…]</c>, invariant, round-trippable).</summary>
    public static string VectorLiteral(float[] vector)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{string.Join(",", vector.Select(static component => component.ToString("R", CultureInfo.InvariantCulture)))}]");
    }

    /// <summary>Reads one <see cref="MemoryFactView"/> from the current row of a cosine search.</summary>
    public static MemoryFactView ReadView(System.Data.Common.DbDataReader reader)
    {
        return ReadViewCore(reader) with { VectorRank = reader.GetFloat(9) };
    }

    /// <summary>
    /// Reads one row of the lexical path: same nine columns as
    /// <see cref="ReadViewCore"/> plus the trailing <c>lexical_rank</c>
    /// real. The caller pairs the row with the same row's vector rank
    /// (zero when the cosine path skipped, real otherwise) to feed the
    /// RRF fusion — see <see cref="EfMemoryStore.SearchAsync"/> and
    /// <c>MemoryHybridRanking</c>.
    /// </summary>
    public static LexicalRankedRow ReadViewWithLexicalRank(System.Data.Common.DbDataReader reader)
    {
        return new LexicalRankedRow(ReadViewCore(reader), reader.GetFloat(9));
    }

    /// <summary>
    /// Reads the nine base columns the hybrid path shares with both the
    /// cosine and lexical queries. The trailing rank column (index 9) is
    /// the cosine similarity on the cosine path and the lexical
    /// <c>ts_rank</c> on the lexical path; the two public readers above
    /// cast it to <see cref="MemoryFactView.VectorRank"/> or
    /// <see cref="LexicalRankedRow.LexicalRank"/> respectively.
    /// </summary>
    internal static MemoryFactView ReadViewCore(System.Data.Common.DbDataReader reader)
    {
        return new MemoryFactView(
            Id: new MemoryFactId(reader.GetGuid(0)),
            Scope: MemoryScopeKeys.Parse(reader.GetString(1))
                ?? throw new InvalidOperationException($"unknown memory scope key '{reader.GetString(1)}'"),
            SubjectId: reader.GetString(2),
            Kind: MemoryFactKindKeys.Parse(reader.GetString(3))
                ?? throw new InvalidOperationException($"unknown memory fact kind key '{reader.GetString(3)}'"),
            TopicKey: reader.GetString(4),
            Text: reader.GetString(5),
            Source: MemorySourceKeys.Parse(reader.GetString(6))
                ?? throw new InvalidOperationException($"unknown memory source key '{reader.GetString(6)}'"),
            CreatedBy: reader.GetString(7),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(8));
    }
}
