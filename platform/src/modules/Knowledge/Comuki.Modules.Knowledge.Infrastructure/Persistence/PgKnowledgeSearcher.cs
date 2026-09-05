using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// pgvector cosine-distance search over <c>memory_embeddings</c>.
/// Returns an empty list when the pgvector extension is unavailable —
/// the migration leaves a graceful <c>RAISE NOTICE</c> path on a plain
/// Postgres image, and the searcher mirrors that contract. Negative or
/// impossible similarity thresholds are rejected at the input boundary.
/// </summary>
public sealed class PgKnowledgeSearcher(
    IDbContextFactory<MemoryDbContext> contextFactory,
    IEmbeddingClient embedder,
    ILogger<PgKnowledgeSearcher> logger) : IKnowledgeSearcher
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        string query,
        Guid? projectId,
        int topK,
        float minSimilarity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (topK is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be in [1, 1000]");
        }

        if (minSimilarity is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(minSimilarity), minSimilarity, "minSimilarity must be in [0.0, 1.0]");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = MemoryEmbeddingSql.EmbeddingColumnExistsSql;
            var available = (bool)(await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (!available)
            {
                logger.LogInformation("knowledge search skipped — pgvector embedding column is absent on this deployment");
                return [];
            }
        }

        var queryVector = await embedder.EmbedAsync(query, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = MemoryEmbeddingSql.CosineSearchSql;
        command.Parameters.Add(new NpgsqlParameter("@vector", NpgsqlDbType.Text)
        {
            Value = MemoryEmbeddingSql.VectorLiteral(queryVector),
        });
        command.Parameters.Add(new NpgsqlParameter("@projectId", NpgsqlDbType.Text)
        {
            Value = (object?)projectId?.ToString() ?? DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("@minSimilarity", NpgsqlDbType.Real)
        {
            Value = minSimilarity,
        });
        command.Parameters.Add(new NpgsqlParameter("@limit", NpgsqlDbType.Integer)
        {
            Value = topK,
        });

        var hits = new List<KnowledgeSearchHit>(topK);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var (id, sourceDocumentId, _, chunkText, _, _, similarity) = MemoryEmbeddingSql.ReadRow(reader);
            hits.Add(new KnowledgeSearchHit(id, sourceDocumentId, chunkText, similarity));
        }

        return hits;
    }
}
