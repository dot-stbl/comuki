using System.Data;
using System.Globalization;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// Raw-SQL surface for the pgvector <c>embedding</c> column: literal
/// formatting, the availability probe, the embedding UPDATE and the
/// cosine-distance SELECT. The column lives outside the EF model on
/// purpose — no EF-pgvector provider, no vector materialized in .NET.
/// </summary>
public static class MemoryFactSql
{
    /// <summary>Availability probe: does the embedding column exist (pgvector was present at migration time)?</summary>
    public const string EmbeddingColumnExistsSql =
        """
        SELECT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'memory_facts' AND column_name = 'embedding')
        """;

    /// <summary>Writes the embedding of one fact row (inside the write transaction).</summary>
    public const string UpdateEmbeddingSql =
        "UPDATE memory_facts SET embedding = @vector::vector WHERE id = @id";

    /// <summary>
    /// Cosine-distance search over embedded, visible facts; NULL filter
    /// parameters widen the scope (they must arrive text-typed — an
    /// untyped NULL parameter fails with 42P08). The vector parameter
    /// carries an untyped literal typed by the explicit <c>::vector</c>
    /// cast.
    /// </summary>
    public const string CosineSearchSql =
        """
        SELECT id, scope, subject_id, kind, topic_key, text, source, created_by, created_at
        FROM memory_facts
        WHERE superseded_at IS NULL
          AND (kind <> 'ephemeral' OR created_at >= @cutoff)
          AND (@scope IS NULL OR scope = @scope)
          AND (@subject IS NULL OR subject_id = @subject)
          AND (@kind IS NULL OR kind = @kind)
          AND embedding IS NOT NULL
        ORDER BY embedding <=> @vector::vector
        LIMIT @limit
        """;

    /// <summary>Formats a vector as a pgvector literal (<c>[1,0.5,…]</c>, invariant, round-trippable).</summary>
    /// <param name="vector"></param>
    public static string VectorLiteral(float[] vector)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{string.Join(",", vector.Select(static component => component.ToString("R", CultureInfo.InvariantCulture)))}]");
    }

    /// <summary>Reads one <see cref="MemoryFactView"/> from the current row of a cosine search.</summary>
    /// <param name="reader"></param>
    public static MemoryFactView ReadView(System.Data.Common.DbDataReader reader)
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
