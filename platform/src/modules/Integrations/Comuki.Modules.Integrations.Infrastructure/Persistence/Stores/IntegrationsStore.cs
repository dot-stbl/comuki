using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Deliveries;
using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Modules.Integrations.Domain.Rules;
using Comuki.Modules.Integrations.Domain.Sync;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Comuki.Modules.Integrations.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="IIntegrationsStore"/> over the <see cref="IntegrationsDbContext"/>.
/// Unique-index conflicts (SQLSTATE 23505) translate into the friendly
/// boolean/nullable contract: the database index is the arbiter, races
/// are safe by construction.
/// </summary>
/// <param name="db">Integrations context of the current scope.</param>
/// <param name="clock">Time source for guarded-update stamps.</param>
public sealed class IntegrationsStore(IntegrationsDbContext db, TimeProvider clock) : IIntegrationsStore
{
    /// <inheritdoc />
    public async Task<SourceConnection?> FindConnectionByWebhookAsync(string sourceKey, string webhookKey, CancellationToken cancellationToken = default)
    {
        return await db.Connections.AsNoTracking()
            .Where(connection => connection.WebhookKey == webhookKey
                && connection.Provider == (TicketProviderKeys.TryParse(sourceKey) ?? TicketProvider.Native)
                && connection.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<SourceConnection?> FindConnectionAsync(SourceConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        return db.Connections.AsNoTracking()
            .Where(connection => connection.Id == connectionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceConnection>> ListConnectionsAsync(ProjectId? projectId, CancellationToken cancellationToken = default)
    {
        var query = db.Connections.AsNoTracking();
        if (projectId is { } scope)
        {
            query = query.Where(connection => connection.ProjectId == scope);
        }

        return await query.OrderBy(connection => connection.CreatedAt).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddConnectionAsync(SourceConnection connection, CancellationToken cancellationToken = default)
    {
        // New connections are detached aggregates; Add attaches them as Added instead of Update treating them as existing rows.
        db.Connections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateConnectionAsync(SourceConnection connection, CancellationToken cancellationToken = default)
    {
        // Updates arrive detached after AsNoTracking reads; the original
        // tracked instance (e.g. from AddConnectionAsync on the same
        // scope) must be detached first — otherwise EF throws on the
        // duplicate-key attach. Then explicitly attach as Modified so EF
        // never acts on a Detached entry.
        DetachTrackedInstance(connection.Id);
        db.Entry(connection).State = EntityState.Modified;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Detaches any tracked instance with the given id so the next
    /// <c>db.Entry(...).State = Modified</c> attach does not collide on
    /// the duplicate-key guard.
    /// </summary>
    /// <param name="connectionId"></param>
    private void DetachTrackedInstance(SourceConnectionId connectionId)
    {
        var tracked = db.ChangeTracker
            .Entries<SourceConnection>()
            .FirstOrDefault(entry => entry.Entity.Id == connectionId);
        tracked?.State = EntityState.Detached;
    }

    /// <summary>
    /// Same as <see cref="DetachTrackedInstance"/> for admission rules —
    /// the rule update path hits the same EF duplicate-key guard when a
    /// prior request left the entity in the tracker.
    /// </summary>
    /// <param name="ruleId"></param>
    private void DetachTrackedRule(AdmissionRuleId ruleId)
    {
        var tracked = db.ChangeTracker
            .Entries<AdmissionRule>()
            .FirstOrDefault(entry => entry.Entity.Id == ruleId);
        tracked?.State = EntityState.Detached;
    }

    /// <inheritdoc />
    public Task RotateSecretAsync(SourceConnection connection, CancellationToken cancellationToken = default)
    {
        // Same write path as UpdateConnectionAsync — declared separately so
        // the service hands a rotation-specific contract to a clearly-named
        // entry point rather than a generic "update anything" mutator.
        return UpdateConnectionAsync(connection, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteConnectionAsync(SourceConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        await db.Connections
            .Where(connection => connection.Id == connectionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdmissionRule>> ListEnabledRulesAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        return await db.Rules.AsNoTracking()
            .Where(rule => rule.ProjectId == projectId && rule.Enabled)
            .OrderBy(rule => rule.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdmissionRule>> ListRulesAsync(ProjectId? projectId, CancellationToken cancellationToken = default)
    {
        var query = db.Rules.AsNoTracking();
        if (projectId is { } scope)
        {
            query = query.Where(rule => rule.ProjectId == scope);
        }

        return await query.OrderBy(rule => rule.CreatedAt).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<AdmissionRule?> FindRuleAsync(AdmissionRuleId ruleId, CancellationToken cancellationToken = default)
    {
        return db.Rules.AsNoTracking()
            .Where(rule => rule.Id == ruleId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddRuleAsync(AdmissionRule rule, CancellationToken cancellationToken = default)
    {
        db.Rules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateRuleAsync(AdmissionRule rule, CancellationToken cancellationToken = default)
    {
        // Same detach-first pattern as UpdateConnectionAsync — Update() on a
        // duplicate-key entity throws on the EF tracker; detach the
        // previously-tracked instance before attaching the new one.
        DetachTrackedRule(rule.Id);
        db.Rules.Update(rule);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteRuleAsync(AdmissionRuleId ruleId, CancellationToken cancellationToken = default)
    {
        await db.Rules
            .Where(rule => rule.Id == ruleId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryInsertDeliveryAsync(Delivery delivery, CancellationToken cancellationToken = default)
    {
        db.Deliveries.Add(delivery);
        return await TrySaveUniqueAsync(delivery, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkDeliveryOutcomeAsync(Guid deliveryId, string outcome, string? detail, CancellationToken cancellationToken = default)
    {
        var delivery = await db.Deliveries.FindAsync([deliveryId], cancellationToken);
        if (delivery is null)
        {
            return;
        }

        delivery.SetOutcome(outcome, detail);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InboundItem?> TryInsertTicketAsync(InboundItem ticket, CancellationToken cancellationToken = default)
    {
        db.Tickets.Add(ticket);
        return await TrySaveUniqueAsync(ticket, cancellationToken) ? ticket : null;
    }

    /// <inheritdoc />
    public async Task AddDismissedTicketAsync(InboundItem ticket, CancellationToken cancellationToken = default)
    {
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<InboundItem?> FindTicketAsync(InboundItemId ticketId, CancellationToken cancellationToken = default)
    {
        return db.Tickets.AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryMarkClaimedAsync(InboundItemId ticketId, RunId runId, CancellationToken cancellationToken = default)
    {
        // guarded set-based write: only a still-pending row flips, a
        // concurrent claim updates zero rows and loses cleanly
        var updated = await db.Tickets
            .Where(ticket => ticket.Id == ticketId && ticket.Status == InboundItemStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(ticket => ticket.Status, InboundItemStatus.Claimed)
                    .SetProperty(ticket => ticket.RunId, runId)
                    .SetProperty(ticket => ticket.UpdatedAt, clock.GetUtcNow()),
                cancellationToken);

        return updated == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboundItem>> ListPendingAsync(ProjectId? projectId, int limit, CancellationToken cancellationToken = default)
    {
        var query = db.Tickets.AsNoTracking()
            .Where(ticket => ticket.Status == InboundItemStatus.Pending);
        if (projectId is { } scope)
        {
            query = query.Where(ticket => ticket.ProjectId == scope);
        }

        return await query
            .OrderByDescending(ticket => ticket.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboundItem>> ListClaimedAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await db.Tickets.AsNoTracking()
            .Where(ticket => ticket.Status == InboundItemStatus.Claimed && ticket.RunId != null)
            .OrderBy(ticket => ticket.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseTicketAsync(InboundItemId ticketId, CancellationToken cancellationToken = default)
    {
        var updated = await db.Tickets
            .Where(ticket => ticket.Id == ticketId && ticket.Status == InboundItemStatus.Claimed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(ticket => ticket.Status, InboundItemStatus.Done)
                    .SetProperty(ticket => ticket.UpdatedAt, clock.GetUtcNow()),
                cancellationToken);

        if (updated == 0)
        {
            throw new InvalidOperationException($"ticket {ticketId} cannot be released — not claimed");
        }
    }

    /// <inheritdoc />
    public async Task EnqueueSyncJobAsync(SyncJob job, CancellationToken cancellationToken = default)
    {
        db.SyncJobs.Add(job);
        await TrySaveUniqueAsync(job, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncJob>> ListDueSyncJobsAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
    {
        return await db.SyncJobs.AsNoTracking()
            .Where(syncJob => syncJob.Status == SyncJobStatus.Pending && syncJob.NextAttemptAt <= now)
            .OrderBy(syncJob => syncJob.NextAttemptAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkSyncJobDoneAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var job = await db.SyncJobs.FindAsync([jobId], cancellationToken);
        if (job is null)
        {
            return;
        }

        job.MarkDone(now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkSyncJobFailedAsync(Guid jobId, string error, int maxAttempts, TimeSpan backoff, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var job = await db.SyncJobs.FindAsync([jobId], cancellationToken);
        if (job is null)
        {
            return;
        }

        job.MarkFailed(error, maxAttempts, backoff, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> TrySaveUniqueAsync(object entity, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            // 23505 = unique_violation: the index already holds this
            // letter/ticket/job — a replay/duplicate, not an error;
            // detach so the scope stays clean
            db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }
}
