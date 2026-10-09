using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IWorkTaskStore"/> over
/// <see cref="WorkDbContext"/>. The aggregate is the single source
/// of truth in memory; the store translates it to entity rows
/// (the Task itself + the side collections) and back, and
/// enforces the <c>bigint</c> Version optimistic-concurrency
/// contract (<c>add-work-management/design.md</c> §Persistence)
/// on every <see cref="SaveAsync"/>.
/// </summary>
public sealed class EfWorkTaskStore(WorkDbContext db, TimeProvider clock) : IWorkTaskStore
{
    /// <inheritdoc />
    public async Task<WorkTask?> FindAsync(WorkTaskId id, CancellationToken cancellationToken = default)
    {
        var entity = await db.WorkTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(task => task.Id == id.Value, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var sourceEntities = await db.WorkTaskSourceRefs
            .AsNoTracking()
            .Where(source => source.TaskId == id.Value)
            .ToListAsync(cancellationToken);
        var dependencyEntities = await db.WorkTaskDependencies
            .AsNoTracking()
            .Where(edge => edge.TaskId == id.Value)
            .ToListAsync(cancellationToken);

        return WorkTaskMapper.ToAggregate(
            entity,
            sourceEntities.Select(WorkTaskMapper.ToAggregate),
            dependencyEntities.Select(WorkTaskMapper.ToAggregate));
    }

    /// <inheritdoc />
    public async Task SaveAsync(WorkTask task, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        // Block A: load the row's persisted Version + ordinal.
        var snapshot = await db.WorkTasks
            .Where(existing => existing.Id == task.Id.Value)
            .Select(static existing => new
            {
                existing.Version,
                existing.AttemptOrdinal,
                existing.ActiveAttemptId,
            })
            .FirstOrDefaultAsync(cancellationToken);
        var loadedVersion = snapshot?.Version;
        var loadedOrdinal = snapshot?.AttemptOrdinal ?? 0;

        var nextVersion = (loadedVersion ?? 0L) + 1L;
        var entity = WorkTaskMapper.ToEntity(task, nextVersion) with { UpdatedAt = now };

        if (loadedVersion is null)
        {
            db.WorkTasks.Add(entity);
        }
        else
        {
            // Optimistic concurrency: tell EF the row's stored
            // Version is the value we just read; a concurrent
            // writer that bumped it under us gets
            // DbUpdateConcurrencyException at SaveChanges.
            db.WorkTasks.Attach(entity);
            db.Entry(entity).Property(static e => e.Version).OriginalValue = loadedVersion.Value;
            db.Entry(entity).State = EntityState.Modified;
        }

        if (loadedVersion is not null)
        {
            await DiffSourceRefsAsync(task, cancellationToken);
            await DiffDependenciesAsync(task, cancellationToken);
        }
        else
        {
            // First save of a freshly-loaded aggregate — there are
            // no persisted rows to diff against; the task's
            // first-authoring pass goes through the standard
            // "insert each one" path. The mapper carries the new
            // id minted by the aggregate's constructors.
            foreach (var source in task.SourceRefs)
            {
                db.WorkTaskSourceRefs.Add(WorkTaskMapper.ToEntity(source, task.Id.Value));
            }

            foreach (var dependency in task.Dependencies)
            {
                db.WorkTaskDependencies.Add(WorkTaskMapper.ToEntity(dependency, task.Id.Value));
            }
        }

        // Block C: attempt ledger — see rules/ef-core.md §6.
        // Every ordinal advance writes a new append-only
        // work_task_attempts row. The terminal-status side handles
        // the active-attempt-cleared path below via in-place
        // mutation (CurrentValues.SetValues) instead of the old
        // Remove+Add pattern — the same-PK insert against an
        // already-tracked row trips EF's identity-map invariants
        // (the row stays in the identity map as Deleted and the
        // Add call claims the same key, surfacing as
        // InvalidOperationException on the first terminal event).
        if (task.AttemptOrdinal.Value > loadedOrdinal
            && task.ActiveAttemptId is { } activeRunId)
        {
            db.WorkTaskAttempts.Add(new WorkTaskAttemptEntity(
                Id: Guid.NewGuid(),
                TaskId: task.Id.Value,
                AttemptOrdinal: task.AttemptOrdinal.Value,
                RunId: activeRunId.Value,
                TerminalStatus: null,
                StartedAt: now,
                TerminalAt: null));
        }
        else if (loadedVersion is not null && task.ActiveAttemptId is null)
        {
            var attempt = await db.WorkTaskAttempts
                .Where(attempt => attempt.TaskId == task.Id.Value
                    && attempt.AttemptOrdinal == task.AttemptOrdinal.Value)
                .FirstOrDefaultAsync(cancellationToken);
            if (attempt is { TerminalStatus: null })
            {
                // In-place mutation: the terminal-stamp update keeps
                // the row's identity stable so downstream derived
                // views that join on attempt.Id see the same value
                // before and after the stamp.
                db.WorkTaskAttempts.Attach(attempt);
                db.Entry(attempt).CurrentValues.SetValues(new
                {
                    attempt.Id,
                    attempt.TaskId,
                    attempt.AttemptOrdinal,
                    attempt.RunId,
                    attempt.StartedAt,
                    TerminalStatus = "Completed",
                    TerminalAt = now,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Side-collection diff for <see cref="WorkTask.SourceRefs"/>.</summary>
    private async Task DiffSourceRefsAsync(WorkTask task, CancellationToken cancellationToken)
    {
        var persisted = await db.WorkTaskSourceRefs
            .Where(source => source.TaskId == task.Id.Value)
            .ToListAsync(cancellationToken);

        var aggregateIds = task.SourceRefs.Select(static source => source.Id).ToHashSet();
        var persistedIds = persisted.Select(static source => source.Id).ToHashSet();

        foreach (var removed in persisted.Where(p => !aggregateIds.Contains(p.Id)))
        {
            db.WorkTaskSourceRefs.Remove(removed);
        }

        foreach (var added in task.SourceRefs.Where(a => !persistedIds.Contains(a.Id)))
        {
            db.WorkTaskSourceRefs.Add(WorkTaskMapper.ToEntity(added, task.Id.Value));
        }

        foreach (var next in task.SourceRefs)
        {
            var current = persisted.FirstOrDefault(p => p.Id == next.Id);
            if (current is null)
            {
                continue;
            }

            if (current.Kind == next.Kind.Value
                && string.Equals(current.ExternalId, next.ExternalId, StringComparison.Ordinal)
                && string.Equals(current.DisplayName, next.DisplayName, StringComparison.Ordinal)
                && current.IsPrimary == next.IsPrimary
                && string.Equals(current.LinkNote, next.LinkNote, StringComparison.Ordinal))
            {
                continue;
            }

            current.Kind = next.Kind.Value;
            current.ExternalId = next.ExternalId;
            current.DisplayName = next.DisplayName;
            current.IsPrimary = next.IsPrimary;
            current.LinkNote = next.LinkNote;
        }
    }

    /// <summary>Side-collection diff for <see cref="WorkTask.Dependencies"/>.</summary>
    private async Task DiffDependenciesAsync(WorkTask task, CancellationToken cancellationToken)
    {
        var persisted = await db.WorkTaskDependencies
            .Where(edge => edge.TaskId == task.Id.Value)
            .ToListAsync(cancellationToken);

        var aggregateIds = task.Dependencies.Select(static edge => edge.Id).ToHashSet();

        foreach (var removed in persisted.Where(p => !aggregateIds.Contains(p.Id)))
        {
            db.WorkTaskDependencies.Remove(removed);
        }

        var persistedIds = persisted.Select(static edge => edge.Id).ToHashSet();
        foreach (var added in task.Dependencies.Where(a => !persistedIds.Contains(a.Id)))
        {
            db.WorkTaskDependencies.Add(WorkTaskMapper.ToEntity(added, task.Id.Value));
        }
    }
}
