using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// pgvector cosine-distance search over <c>knowledge.memory_embeddings</c>.
/// Returns an empty list when the pgvector extension is unavailable —
/// the migration leaves a graceful <c>RAISE NOTICE</c> path on a plain
/// Postgres image, and the searcher mirrors that contract. Negative or
/// impossible similarity thresholds are rejected at the input boundary.
/// The query itself is raw SQL against the pgvector column — outside
/// EF's model — so <c>KnowledgeDbContext</c>'s <c>HasQueryFilter</c>
/// cannot reach it; the ambient subject scope is re-applied directly
/// here instead (see <see cref="EmbeddingSql.CosineSearchSql"/>).
/// </summary>
/// <param name="contextFactory"></param>
/// <param name="embedder"></param>
/// <param name="scopeAccessor">
/// The ambient caller scope — the same one <c>KnowledgeDbContext</c>
/// reads for its EF-tracked entities. Required (not optional like the
/// DbContext's own parameter): this class is only ever resolved through
/// the host DI container, never constructed directly by design-time
/// tooling, so there is no "no accessor available" case to default.
/// </param>
/// <param name="logger"></param>
public sealed class PgKnowledgeSearcher(
    IDbContextFactory<KnowledgeDbContext> contextFactory,
    IEmbeddingClient embedder,
    ISubjectScopeAccessor scopeAccessor,
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
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("query required");
        }
        if (topK is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be in [1, 1000]");
        }

        if (minSimilarity is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(minSimilarity), minSimilarity, "minSimilarity must be in [0.0, 1.0]");
        }

        var scope = scopeAccessor.Current;
        if (projectId is { } requestedProject && !scope.Allows(new ProjectId(requestedProject)))
        {
            // The caller named a project outside their own scope. Refuse
            // rather than run the query: an error (404/403) would confirm
            // the project exists at all, which is itself information a
            // restricted caller is not entitled to — silence reads exactly
            // like "no hits above threshold".
            logger.LogInformation(
                "knowledge search for project {ProjectId} returned nothing — outside the caller's scope",
                requestedProject);
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = EmbeddingSql.EmbeddingColumnExistsSql;
            var available = (bool)(await probe.ExecuteScalarAsync(cancellationToken))!;
            if (!available)
            {
                logger.LogInformation("knowledge search skipped — pgvector embedding column is absent on this deployment");
                return [];
            }
        }

        var queryVector = await embedder.EmbedAsync(query, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = EmbeddingSql.CosineSearchSql;
        command.Parameters.Add(new NpgsqlParameter("@vector", NpgsqlDbType.Text)
        {
            Value = EmbeddingSql.VectorLiteral(queryVector),
        });
        command.Parameters.Add(new NpgsqlParameter("@projectId", NpgsqlDbType.Text)
        {
            Value = (object?)projectId?.ToString() ?? DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("@unrestricted", NpgsqlDbType.Boolean)
        {
            Value = scope.Unrestricted,
        });
        command.Parameters.Add(new NpgsqlParameter("@allowedProjectIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            // Always a real array, never DBNull — `= ANY(NULL)` evaluates
            // to NULL (not true) in Postgres, which would silently widen
            // to "nothing" instead of the intended "nothing outside the
            // global corpus" for a restricted caller with no assignments.
            Value = scope.ProjectIds.Select(static id => id.Value).ToArray(),
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var (id, sourceDocumentId, _, chunkText, _, _, similarity) = EmbeddingSql.ReadRow(reader);
            hits.Add(new KnowledgeSearchHit(id, sourceDocumentId, chunkText, similarity));
        }

        return hits;
    }
}
