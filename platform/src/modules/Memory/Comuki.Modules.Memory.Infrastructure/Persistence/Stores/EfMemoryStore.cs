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

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

// canon judgement #4: the file-static helper types at the bottom of this file stay co-located with EfMemoryStore — they are this one store's tightly coupled persistence mechanics (the 300-line trigger targets business classes, not a store plus its SQL/query helpers).

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
        // Mission id is the strong-typed shape of the "subject" on a
        // mission-scope fact. When the caller passes MissionId, the
        // effective scope is forced to Mission and the effective subject
        // id is the mission guid as a string — the existing scope / subject
        // filter (LINQ + raw SQL) then narrows to one mission's rows.
        // A query with both Scope != Mission and a non-null MissionId
        // is contradictory: refuse before opening a context.
        var effective = MemoryFactMissionQuery.Normalize(query);

        if (!MemoryFactScopeReachability.IsQueryReachable(scopeAccessor, effective.Scope, effective.SubjectId))
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
        var now = clock.GetUtcNow();
        var cutoff = now - MemoryFactPolicy.EphemeralTtl;

        // Hybrid retrieval: rank candidates by both lexical
        // (ts_rank over the generated text_tsv column) and vector
        // (1 − cosine distance over the pgvector embedding) when those
        // paths are available, fused via RRF (k=60). Either path is
        // optional — if the embedding column is missing, or the FTS
        // column is missing, or the user supplied no embedding, the
        // corresponding side returns zero and the other side wins.
        // The freshness fallback at the bottom is the contract's hard
        // floor — no embeddings and no FTS at all still returns
        // ranked rows.
        var lexicalRows = Array.Empty<MemoryFactView>();
        var vectorRows = Array.Empty<MemoryFactView>();

        // Gate the probes on at least one input: when both paths are
        // guaranteed empty (no embedding, no text) the column-existence
        // checks would be pure overhead. They also fail on the
        // in-memory provider used by unit tests, which doesn't expose
        // OpenConnectionAsync — the gate keeps the unit suite green.
        if (query.Embedding is not null || !string.IsNullOrEmpty(query.Text))
        {
            if (query.Embedding is { } embedding
                && await MemoryFactVectors.HasColumnAsync(db, cancellationToken))
            {
                var byCosine = await MemoryFactVectors.TrySearchCosineAsync(db, embedding, MemoryFactMissionQuery.Effective(query, effective.Scope, effective.SubjectId), cutoff, logger, cancellationToken);
                if (byCosine is { Count: > 0 })
                {
                    // The cosine SQL already populates VectorRank = 1 -
                    // distance (MemoryFactSql.ReadView reads column 9); the
                    // position in the list is the rank — RRF reads position,
                    // the surfaced score is just for the manifest / display.
                    vectorRows = [.. byCosine];
                }
            }

            if (await MemoryFactLexical.HasColumnAsync(db, cancellationToken))
            {
                // The lex path uses the user-supplied query text as
                // plainto_tsquery input. MemoryFactLexical.TrySearchLexicalAsync
                // owns the empty-query gate (it short-circuits to [] on
                // a blank/empty query, since an empty tsquery matches
                // nothing and the vector path stays the sole ranker), so
                // the caller does not pre-filter on query.Text being
                // non-empty.
                var byLexical = await MemoryFactLexical.TrySearchLexicalAsync(
                    db, query.Text, MemoryFactMissionQuery.Effective(query, effective.Scope, effective.SubjectId), logger, cancellationToken);
                if (byLexical is not null)
                {
                    lexicalRows = [.. byLexical.Select(static row => row.View with { LexicalRank = row.LexicalRank })];
                }
            }
        }

        if (lexicalRows.Length > 0 || vectorRows.Length > 0)
        {
            var fused = MemoryHybridRanking.Fuse(lexicalRows, vectorRows, limit: query.Limit);
            await MemoryFactReadTracking.RegisterReadsAsync(db, fused.Select(static fact => fact.Id), now, cancellationToken);
            return fused;
        }

        var visible = await MemoryFactQueries.LoadVisibleAsync(
            db,
            effective.Scope,
            effective.SubjectId,
            query.Kind,
            cutoff,
            query.Limit,
            0,
            cancellationToken);
        var results = MemoryFallbackRanking.Rank(visible.Select(MemoryFactViewMapper.Of), query.Limit);
        await MemoryFactReadTracking.RegisterReadsAsync(db, results.Select(static fact => fact.Id), now, cancellationToken);
        return results;
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
        if (await db.MemoryFacts.FirstOrDefaultAsync(fact => fact.Id == id, cancellationToken) is not { } fact)
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

    /// <inheritdoc />
    public async Task<int> PromoteReadFactsAsync(DateTimeOffset now, int readThreshold, TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.MemoryFacts
            .Where(MemoryFactConsolidation.PromoteCandidates(now, readThreshold, minAge))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(fact => fact.Kind, MemoryFactKind.Standing),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DecayUnreadFactsAsync(DateTimeOffset now, TimeSpan unreadWindow, CancellationToken cancellationToken = default)
    {
        var decayedCreatedAt = MemoryFactPolicy.DecayCreatedAt(now);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.MemoryFacts
            .Where(MemoryFactConsolidation.DecayCandidates(now, unreadWindow))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(fact => fact.Kind, MemoryFactKind.Ephemeral)
                    .SetProperty(fact => fact.CreatedAt, decayedCreatedAt),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountActiveFactsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.MemoryFacts.CountAsync(fact => fact.SupersededAt == null, cancellationToken);
    }
}

/// <summary>Scope reachability gate shared by the store's read paths (out-of-scope requests are refused before a context opens).</summary>
file static class MemoryFactScopeReachability
{
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
    public static bool IsQueryReachable(ISubjectScopeAccessor? scopeAccessor, MemoryScope? requestedScope, string? requestedSubjectId)
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
            // Mission facts: a restricted caller (user / project-assigned) has
            // no Mission axis on SubjectScope to validate against, so the
            // safe default is "no mission access at all". An unrestricted
            // system consumer may pass a mission id and read its rows;
            // without a mission id the call is fail-closed at the SQL
            // filter (subject_id is null) and the gate above refuses it
            // preemptively.
            MemoryScope.Mission when requestedSubjectId is null => false,
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
    public static async Task<IReadOnlyList<MemoryFact>> LoadVisibleAsync(
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
/// Access tracking: bumps <c>read_count</c> / <c>last_read_at</c> of the
/// facts a search just returned. Runs on the search's own context AFTER
/// the results are materialized — two cheap statements (load by id,
/// save), never part of the ranking itself, so the read path is not
/// re-ranked or delayed by anything heavier. A tracked load + SaveChanges
/// (not <c>ExecuteUpdateAsync</c>) on purpose: the entity method is the
/// single definition of a "read" and the InMemory-provider unit suite
/// exercises this exact path.
/// </summary>
file static class MemoryFactReadTracking
{
    public static async Task RegisterReadsAsync(
        MemoryDbContext db,
        IEnumerable<MemoryFactId> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var idList = ids.ToArray();
        if (idList.Length == 0)
        {
            return;
        }

        var facts = await db.MemoryFacts
            .Where(fact => idList.Contains(fact.Id))
            .ToListAsync(cancellationToken);
        foreach (var fact in facts)
        {
            fact.RegisterRead(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
