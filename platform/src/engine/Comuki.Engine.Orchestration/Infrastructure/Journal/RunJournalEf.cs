using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Engine.Orchestration.Infrastructure.Journal;

/// <summary>
/// EF implementation of <see cref="IRunJournal"/> over the
/// <see cref="OrchestrationDbContext"/> run_events table. Reads are no-tracking
/// and ordered by the timeline index (occurred_at, id).
/// </summary>
/// <param name="db"></param>
public sealed class RunJournalEf(OrchestrationDbContext db) : IRunJournal
{
    /// <inheritdoc />
    public async Task AppendAsync(RunEventEntry entry, CancellationToken cancellationToken = default)
    {
        db.RunEvents.Add(RunEvent.Create(entry.RunId, entry.Type, entry.PayloadJson, entry.OccurredAt));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(
        RunId runId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            throw new ArgumentException("page is 1-based and must be positive", nameof(page));
        }

        if (pageSize < 1)
        {
            throw new ArgumentException("page size must be positive", nameof(pageSize));
        }

        var entries = await db.RunEvents
            .AsNoTracking()
            .Where(runEvent => runEvent.RunId == runId)
            .OrderBy(runEvent => runEvent.OccurredAt)
            .ThenBy(runEvent => runEvent.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return [.. entries.Select(static runEvent => new RunEventEntry(
            runEvent.Id,
            runEvent.RunId,
            runEvent.Type,
            runEvent.Payload,
            runEvent.OccurredAt))];
    }
}
