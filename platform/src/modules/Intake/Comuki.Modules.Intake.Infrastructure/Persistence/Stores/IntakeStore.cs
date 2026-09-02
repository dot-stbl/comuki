using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Deliveries;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Sync;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Comuki.Modules.Intake.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="IIntakeStore"/> over the <see cref="IntakeDbContext"/>.
/// Unique-index conflicts (SQLSTATE 23505) translate into the friendly
/// boolean/nullable contract: the database index is the arbiter, races
/// are safe by construction.
/// </summary>
/// <param name="db">Intake context of the current scope.</param>
/// <param name="clock">Time source for guarded-update stamps.</param>
public sealed class IntakeStore(IntakeDbContext db, TimeProvider clock) : IIntakeStore
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
        db.Connections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateConnectionAsync(SourceConnection connection, CancellationToken cancellationToken = default)
    {
        db.Connections.Update(connection);
        await db.SaveChangesAsync(cancellationToken);
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
    public async Task<bool> TryInsertDeliveryAsync(IntakeDelivery delivery, CancellationToken cancellationToken = default)
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
    public async Task<IncomingTicket?> TryInsertTicketAsync(IncomingTicket ticket, CancellationToken cancellationToken = default)
    {
        db.Tickets.Add(ticket);
        return await TrySaveUniqueAsync(ticket, cancellationToken) ? ticket : null;
    }

    /// <inheritdoc />
    public async Task AddDismissedTicketAsync(IncomingTicket ticket, CancellationToken cancellationToken = default)
    {
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IncomingTicket?> FindTicketAsync(IncomingTicketId ticketId, CancellationToken cancellationToken = default)
    {
        return db.Tickets.AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryMarkClaimedAsync(IncomingTicketId ticketId, RunId runId, CancellationToken cancellationToken = default)
    {
        // guarded set-based write: only a still-pending row flips, a
        // concurrent claim updates zero rows and loses cleanly
        var updated = await db.Tickets
            .Where(ticket => ticket.Id == ticketId && ticket.Status == IntakeTicketStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(ticket => ticket.Status, IntakeTicketStatus.Claimed)
                    .SetProperty(ticket => ticket.RunId, runId)
                    .SetProperty(ticket => ticket.UpdatedAt, clock.GetUtcNow()),
                cancellationToken);

        return updated == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncomingTicket>> ListPendingAsync(ProjectId? projectId, int limit, CancellationToken cancellationToken = default)
    {
        var query = db.Tickets.AsNoTracking()
            .Where(ticket => ticket.Status == IntakeTicketStatus.Pending);
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
    public async Task<IReadOnlyList<IncomingTicket>> ListClaimedAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await db.Tickets.AsNoTracking()
            .Where(ticket => ticket.Status == IntakeTicketStatus.Claimed && ticket.RunId != null)
            .OrderBy(ticket => ticket.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseTicketAsync(IncomingTicketId ticketId, CancellationToken cancellationToken = default)
    {
        var updated = await db.Tickets
            .Where(ticket => ticket.Id == ticketId && ticket.Status == IntakeTicketStatus.Claimed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(ticket => ticket.Status, IntakeTicketStatus.Done)
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
