using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Ranking;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// EF/Npgsql implementation of <see cref="IMemoryStore"/>. Every method
/// opens its own context from the factory (the store is a safe singleton),
/// writes supersede same-topic rows inside one transaction, and searches
/// cosine-ranked when a query embedding is supplied and the pgvector
/// column exists — everything else falls back to the embedding-free
/// ranking, which is the contract's hard floor.
/// </summary>
/// <param name="dbFactory"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class EfMemoryStore(
    IDbContextFactory<MemoryDbContext> dbFactory,
    TimeProvider clock,
    ILogger<EfMemoryStore> logger) : IMemoryStore
{
    /// <inheritdoc />
    public async Task<MemoryFactView> WriteAsync(MemoryFactWrite write, CancellationToken cancellationToken = default)
    {
        if (write.Embedding is { } embedding && embedding.Length != MemoryFactPolicy.EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"embedding must have {MemoryFactPolicy.EmbeddingDimensions} dimensions, got {embedding.Length}",
                nameof(write));
        }

        var now = clock.GetUtcNow();
        var subject = MemoryFact.CanonicalKey(write.SubjectId);
        var topic = MemoryFact.CanonicalKey(write.TopicKey);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            // supersede-first inside the transaction keeps at most one
            // active row per topic even under concurrent writers (the
            // partial unique index is the last line of defense)
            _ = await db.MemoryFacts
                .Where(fact => fact.Scope == write.Scope
                    && fact.SubjectId == subject
                    && fact.TopicKey == topic
                    && fact.SupersededAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(fact => fact.SupersededAt, now),
                    cancellationToken);

            var fact = MemoryFact.Create(
                write.Scope, subject, write.Kind, topic, write.Text, write.Source, write.CreatedBy, now);
            _ = db.MemoryFacts.Add(fact);
            await db.SaveChangesAsync(cancellationToken);

            if (write.Embedding is { } vector)
            {
                // no pgvector at migration time ⇒ no embedding column: the
                // fact still lands, searchable via the fallback ranking
                // (the add-chat-memory contract's hard floor). When the
                // column exists the attach is part of the write
                // transaction — a failure there rolls the whole write back
                // (a failed statement poisons the transaction; swallowing
                // it would turn the commit into a silent rollback).
                if (await MemoryFactVectors.HasColumnAsync(db, cancellationToken))
                {
                    await MemoryFactVectors.AttachAsync(db, fact.Id, vector, cancellationToken);
                }
                else
                {
                    logger.LogWarning("embedding column unavailable; fact stored without a vector");
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return MemoryFactViewMapper.Of(fact);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryFactView>> SearchAsync(MemoryFactQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = clock.GetUtcNow() - MemoryFactPolicy.EphemeralTtl;

        if (query.Embedding is { } embedding
            && await MemoryFactVectors.HasColumnAsync(db, cancellationToken))
        {
            var byCosine = await MemoryFactVectors.TrySearchCosineAsync(db, embedding, query, cutoff, logger, cancellationToken);
            if (byCosine is { Count: > 0 })
            {
                return byCosine;
            }
        }

        var visible = await MemoryFactQueries.LoadVisibleAsync(db, query.Scope, query.SubjectId, query.Kind, cutoff, cancellationToken);
        return MemoryFallbackRanking.Rank(visible.Select(MemoryFactViewMapper.Of), query.Limit);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryFactView>> ListAsync(MemoryScope scope, string subjectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = clock.GetUtcNow() - MemoryFactPolicy.EphemeralTtl;
        var visible = await MemoryFactQueries.LoadVisibleAsync(db, scope, MemoryFact.CanonicalKey(subjectId), null, cutoff, cancellationToken);
        return MemoryFallbackRanking.Rank(visible.Select(MemoryFactViewMapper.Of), int.MaxValue);
    }

    /// <inheritdoc />
    public async Task<bool> ForgetAsync(MemoryFactId id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var fact = await db.MemoryFacts.FirstOrDefaultAsync(fact => fact.Id == id, cancellationToken);
        if (fact is null)
        {
            return false;
        }

        _ = db.MemoryFacts.Remove(fact);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var cutoff = now - MemoryFactPolicy.EphemeralTtl;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.MemoryFacts
            .Where(fact => fact.Kind == MemoryFactKind.Ephemeral && fact.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>Entity → view projection, one place for both read paths.</summary>
file static class MemoryFactViewMapper
{
    public static MemoryFactView Of(MemoryFact fact)
    {
        return new MemoryFactView(
            fact.Id,
            fact.Scope,
            fact.SubjectId,
            fact.Kind,
            fact.TopicKey,
            fact.Text,
            fact.Source,
            fact.CreatedBy,
            fact.CreatedAt);
    }
}

/// <summary>The visible-facts EF query shared by search and list (superseded and expired excluded).</summary>
file static class MemoryFactQueries
{
    public static async Task<List<MemoryFact>> LoadVisibleAsync(
        MemoryDbContext db,
        MemoryScope? scope,
        string? subjectId,
        MemoryFactKind? kind,
        DateTimeOffset ephemeralCutoff,
        CancellationToken cancellationToken)
    {
        return await db.MemoryFacts
            .AsNoTracking()
            .Where(fact => fact.SupersededAt == null)
            .Where(fact => scope == null || fact.Scope == scope)
            .Where(fact => subjectId == null || fact.SubjectId == subjectId)
            .Where(fact => kind == null || fact.Kind == kind)
            .Where(fact => fact.Kind != MemoryFactKind.Ephemeral || fact.CreatedAt >= ephemeralCutoff)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Raw ADO surface for the pgvector embedding column: attach, the
/// availability probe and the cosine search. SQL text lives in
/// <see cref="MemoryFactSql"/>; parameters are always bound, never
/// interpolated.
/// </summary>
file static class MemoryFactVectors
{
    public static async Task AttachAsync(
        MemoryDbContext db,
        MemoryFactId id,
        float[] vector,
        CancellationToken cancellationToken)
    {
        // the write transaction keeps the connection open — the update
        // joins it, so a crash rolls the fact and its vector back together
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = MemoryFactSql.UpdateEmbeddingSql;
        _ = command.Parameters.Add(new NpgsqlParameter("id", id.Value));
        _ = command.Parameters.Add(VectorParameter("vector", vector));
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<bool> HasColumnAsync(MemoryDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = MemoryFactSql.EmbeddingColumnExistsSql;
            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public static async Task<IReadOnlyList<MemoryFactView>?> TrySearchCosineAsync(
        MemoryDbContext db,
        float[] embedding,
        MemoryFactQuery query,
        DateTimeOffset ephemeralCutoff,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                var connection = db.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
                command.CommandText = MemoryFactSql.CosineSearchSql;
                _ = command.Parameters.Add(VectorParameter("vector", embedding));
                _ = command.Parameters.Add(new NpgsqlParameter("cutoff", ephemeralCutoff));
                _ = command.Parameters.Add(FilterTextParameter(
                    "scope", query.Scope is { } scope ? MemoryScopeKeys.Key(scope) : null));
                _ = command.Parameters.Add(FilterTextParameter(
                    "subject", query.SubjectId is null ? null : MemoryFact.CanonicalKey(query.SubjectId)));
                _ = command.Parameters.Add(FilterTextParameter(
                    "kind", query.Kind is { } kind ? MemoryFactKindKeys.Key(kind) : null));
                _ = command.Parameters.Add(new NpgsqlParameter("limit", query.Limit));

                var rows = new List<MemoryFactView>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(MemoryFactSql.ReadView(reader));
                }

                return rows;
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (PostgresException exception)
        {
            // the probe said the column exists but the query disagrees
            // (migration drift) — memory answers via the fallback instead
            logger.LogWarning(exception, "cosine search failed; falling back to freshness ranking");
            return null;
        }
    }

    public static NpgsqlParameter VectorParameter(string name, float[] vector)
    {
        // Unknown-typed parameter: PostgreSQL treats the value as an
        // untyped literal and the explicit ::vector cast in the SQL types
        // it (a bare unknown parameter in the operator position fails
        // with 42P08). An explicitly text-typed parameter has no cast to
        // vector and kills the statement.
        return new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Unknown)
        {
            Value = MemoryFactSql.VectorLiteral(vector),
        };
    }

    public static NpgsqlParameter FilterTextParameter(string name, string? value)
    {
        // the @param IS NULL filters need a TYPED null — an untyped
        // DBNull parameter fails with 42P08 (data type undetermined)
        return new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = value is null ? DBNull.Value : value,
        };
    }
}
