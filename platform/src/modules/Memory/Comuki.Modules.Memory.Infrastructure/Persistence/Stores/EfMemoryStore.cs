using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Ranking;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
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
/// ranking, which is the contract's hard floor. The cosine path is raw
/// SQL against the pgvector column, outside EF's model, so
/// <see cref="MemoryDbContext"/>'s <c>HasQueryFilter</c> cannot reach it —
/// <see cref="SearchAsync"/> refuses a plainly out-of-scope request before
/// ever opening the context, and <c>MemoryFactSql.CosineSearchSql</c>
/// carries the same rule directly for whatever reaches the SQL anyway.
/// </summary>
/// <param name="dbFactory"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
/// <param name="scopeAccessor">
/// The ambient caller scope — read directly here (not only through the
/// <see cref="MemoryDbContext"/> the factory hands back) so a plainly
/// out-of-scope search can be refused before a context is even opened.
/// Optional, mirroring <see cref="MemoryDbContext"/>'s own accessor
/// parameter: a store built without one is by definition a system
/// consumer and searches unrestricted; a host that cares about scoping
/// registers a real accessor and this reads it the same way the
/// DbContext's query filter does.
/// </param>
public sealed class EfMemoryStore(
    IDbContextFactory<MemoryDbContext> dbFactory,
    TimeProvider clock,
    ILogger<EfMemoryStore> logger,
    ISubjectScopeAccessor? scopeAccessor = null) : IMemoryStore
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
            await db.MemoryFacts
                .Where(fact => fact.Scope == write.Scope
                    && fact.SubjectId == subject
                    && fact.TopicKey == topic
                    && fact.SupersededAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(fact => fact.SupersededAt, now),
                    cancellationToken);

            // a backdated CreatedAt is the custom-TTL contract; supersede
            // stamps still use the real clock so audit reflects when the
            // replacement actually happened
            var fact = MemoryFact.Create(
                write.Scope, subject, write.Kind, topic, write.Text, write.Source, write.CreatedBy,
                write.CreatedAt ?? now);
            db.MemoryFacts.Add(fact);
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
        if (!IsQueryReachable(scopeAccessor, query.Scope, query.SubjectId))
        {
            // A restricted caller naming a project it is not assigned to,
            // or the user scope at all (no per-user identity axis exists
            // to check it against), can never get a row back — refuse
            // before opening a context rather than let the cosine path's
            // raw SQL or the LINQ fallback's query filter discover that on
            // its own.
            return [];
        }

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

        var visible = await MemoryFactQueries.LoadVisibleAsync(
            db,
            query.Scope,
            query.SubjectId,
            query.Kind,
            cutoff,
            query.Limit,
            0,
            cancellationToken);
        return MemoryFallbackRanking.Rank(visible.Select(MemoryFactViewMapper.Of), query.Limit);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryFactView>> ListAsync(
        MemoryScope scope,
        string subjectId,
        int limit = IMemoryStore.DefaultListLimit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = clock.GetUtcNow() - MemoryFactPolicy.EphemeralTtl;
        var visible = await MemoryFactQueries.LoadVisibleAsync(
            db,
            scope,
            MemoryFact.CanonicalKey(subjectId),
            null,
            cutoff,
            limit,
            offset,
            cancellationToken);
        return MemoryFallbackRanking.Rank(visible.Select(MemoryFactViewMapper.Of), limit);
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

        db.MemoryFacts.Remove(fact);
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

    /// <summary>
    /// Whether <paramref name="scopeAccessor"/>'s current scope can ever
    /// see a row matching <paramref name="requestedScope"/>/
    /// <paramref name="requestedSubjectId"/>. No accessor at all (a store
    /// built without one) is by definition a system consumer, same as
    /// <see cref="MemoryDbContext"/>'s own default; an unrestricted
    /// established scope always can too. A restricted caller can never
    /// reach <see cref="MemoryScope.User"/> (no per-user identity axis
    /// exists on <see cref="SubjectScope"/> to check it against — the
    /// same fail-closed default <see cref="MemoryDbContext"/>'s query
    /// filter applies) nor a <see cref="MemoryScope.Project"/> id outside
    /// its own assignments. Anything else (no scope named, or
    /// <see cref="MemoryScope.Global"/>) proceeds — the EF query filter
    /// and, on the cosine path, <c>MemoryFactSql.CosineSearchSql</c>'s own
    /// clause narrow the rest.
    /// </summary>
    private static bool IsQueryReachable(ISubjectScopeAccessor? scopeAccessor, MemoryScope? requestedScope, string? requestedSubjectId)
    {
        if (scopeAccessor is null)
        {
            return true;
        }

        var scope = scopeAccessor.Current;
        return scope.Unrestricted || requestedScope switch
        {
            MemoryScope.User => false,
            MemoryScope.Project when requestedSubjectId is { } subjectId
                && Guid.TryParse(subjectId, out var projectId) => scope.Allows(new ProjectId(projectId)),
            _ => true,
        };
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
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        return await db.MemoryFacts
            .AsNoTracking()
            .Where(fact => fact.SupersededAt == null)
            .Where(fact => scope == null || fact.Scope == scope)
            .Where(fact => subjectId == null || fact.SubjectId == subjectId)
            .Where(fact => kind == null || fact.Kind == kind)
            .Where(fact => fact.Kind != MemoryFactKind.Ephemeral || fact.CreatedAt >= ephemeralCutoff)
            .Skip(offset)
            .Take(limit)
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
        command.Parameters.Add(new NpgsqlParameter("id", id.Value));
        command.Parameters.Add(VectorParameter("vector", vector));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                command.Parameters.Add(VectorParameter("vector", embedding));
                command.Parameters.Add(new NpgsqlParameter("cutoff", ephemeralCutoff));
                // Object-axis scoping — this query runs raw SQL outside
                // EF's model, so MemoryDbContext's HasQueryFilter on
                // MemoryFact cannot reach it; these two parameters
                // reproduce the same rule directly (see MemoryFactSql.CosineSearchSql).
                command.Parameters.Add(new NpgsqlParameter("unrestricted", NpgsqlTypes.NpgsqlDbType.Boolean)
                {
                    Value = db.ScopeUnrestricted,
                });
                command.Parameters.Add(new NpgsqlParameter("allowedProjectSubjectKeys", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
                {
                    Value = db.ScopeProjectSubjectKeys,
                });
                command.Parameters.Add(FilterTextParameter(
                    "scope", query.Scope is { } scope ? MemoryScopeKeys.Key(scope) : null));
                command.Parameters.Add(FilterTextParameter(
                    "subject", query.SubjectId is null ? null : MemoryFact.CanonicalKey(query.SubjectId)));
                command.Parameters.Add(FilterTextParameter(
                    "kind", query.Kind is { } kind ? MemoryFactKindKeys.Key(kind) : null));
                command.Parameters.Add(new NpgsqlParameter("limit", query.Limit));

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
