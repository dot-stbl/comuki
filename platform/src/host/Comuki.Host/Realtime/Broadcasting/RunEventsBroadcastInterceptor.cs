using System.Collections.Concurrent;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Shared.Contracts.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Comuki.Host.Realtime.Broadcasting;

/// <summary>
/// <see cref="ISaveChangesInterceptor"/> on the orchestration context: the
/// single choke point every journal append passes through — including the
/// queue's and the reaper's direct <c>RunEvents.Add</c> writes, which do
/// not go through <see cref="IRunJournal"/>. Entries are captured at
/// saving, broadcast only after the save succeeded, and dropped when the
/// save fails, so a failed write never reaches the wire. Async interception
/// only — every platform writer calls <c>SaveChangesAsync</c>.
/// </summary>
/// <param name="broadcaster"></param>
/// <param name="logger"></param>
public sealed class RunEventsBroadcastInterceptor(
    IRunEventsBroadcaster broadcaster,
    ILogger<RunEventsBroadcastInterceptor> logger) : SaveChangesInterceptor
{
    private readonly ConcurrentDictionary<DbContext, IReadOnlyList<RunEventEntry>> pending = new();

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var entries = context.ChangeTracker.Entries<RunEvent>()
                .Where(static entry => entry.State == EntityState.Added)
                .Select(static entry => new RunEventEntry(
                    entry.Entity.Id,
                    entry.Entity.RunId,
                    entry.Entity.Type,
                    entry.Entity.Payload,
                    entry.Entity.OccurredAt))
                .ToArray();

            if (entries.Length > 0)
            {
                pending[context] = entries;
            }
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && pending.TryRemove(context, out var entries))
        {
            await RunEventsBroadcastSender.SendAsync(broadcaster, logger, entries, cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            _ = pending.TryRemove(context, out _);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Best-effort delivery step: a broadcast failure is logged and swallowed —
/// it must never surface as a failing SaveChanges in the writer.
/// </summary>
file static class RunEventsBroadcastSender
{
    public static async Task SendAsync(
        IRunEventsBroadcaster broadcaster,
        ILogger logger,
        IReadOnlyList<RunEventEntry> entries,
        CancellationToken cancellationToken)
    {
        try
        {
            await broadcaster.BroadcastAsync(entries, cancellationToken);
        }
        catch (Exception exception)
        {
            // boundary: realtime delivery is best-effort by contract; the
            // journal rows are already saved and remain readable over REST
            logger.LogError(
                exception,
                "Failed to broadcast {Count} journal entries to the realtime groups",
                entries.Count);
        }
    }
}
