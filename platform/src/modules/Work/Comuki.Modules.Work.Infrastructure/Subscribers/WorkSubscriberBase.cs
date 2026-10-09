using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Infrastructure.Subscribers;

/// <summary>
/// Per-cycle engine-outbox subscriber seam — replaces the five
/// copy-pasted <c>watermark → poll → foreach → per-row → advance</c>
/// loops with one walked shape (the framework duplication the
/// previous review flagged). The base owns:
/// <list type="bullet">
///   <item>the per-cycle DI scope (the seam was leaking the
///         <see cref="Inbox.IWorkInboxWatermarkStore"/> as a
///         singleton-captured scoped DbContext — the captive
///         dependency that left <c>watermark.Record</c> and
///         <c>Get</c> walking the same long-lived scope);</item>
///   <item>the watermark read (<c>GetAsync</c> from the per-cycle
///         scope) and write (<c>RecordAsync</c> — a single
///         <c>INSERT … ON CONFLICT … DO UPDATE GREATEST(...)</c>
///         statement that holds the monotonic-direction contract
///         on the SQL side);</item>
///   <item>the row walk over the engine outbox via the
///         <see cref="IOrchestrationOutboxReader"/> port — the
///         Work module never imports
///         <c>Comuki.Engine.Orchestration</c> types; the
///         reader projection
///         (<see cref="OrchestrationOutboxRow"/>) is the only
///         shape the subscribers see.</item>
/// </list>
/// Concrete subclasses override <see cref="HandleRowAsync"/> /
/// <see cref="ShouldProcessRow"/>; the row walk, watermark
/// advance, structured-log summary and worker outcome live here.
/// </summary>
public abstract class WorkSubscriberBase(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger subscriberLogger) : IComukiWorker
{
    /// <summary>The per-cycle scope's resolved TimeProvider — surfaced as <see cref="Clock"/> for the per-row work.</summary>
    protected TimeProvider Clock { get; } = clock;

    /// <summary>Structured logger — surfaced for the per-cycle summary line.</summary>
    protected ILogger Logger { get; } = subscriberLogger;

    /// <summary>The Work-side stable name — surfaced in <see cref="WorkerResult.Detail"/> and the per-cycle log line.</summary>
    public abstract string Name { get; }

    /// <summary>Polling cadence — every subscriber polls the engine outbox on the same 5s interval.</summary>
    public virtual WorkerSchedule Schedule => WorkerSchedule.Interval(TimeSpan.FromSeconds(5));

    /// <summary>The engine-outbox <c>type</c>(s) the worker is bound to (the SQL filter).</summary>
    /// <remarks>Set per cycle from the per-cycle scope (today the type lists are file-static on the subclass).</remarks>
    protected abstract IReadOnlyCollection<string> GetSubscribedTypes();

    /// <summary>The watermark key — a single counter covers the subscribed types when dedupe is shared.</summary>
    protected abstract string WatermarkKey { get; }

    /// <inheritdoc />
    public virtual async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IOrchestrationOutboxReader>();
            var watermarks = scope.ServiceProvider.GetRequiredService<Inbox.IWorkInboxWatermarkStore>();

            var previousWatermark = await watermarks.GetAsync(WatermarkKey, cancellationToken);
            var batch = await reader.PollAsync(
                GetSubscribedTypes(),
                previousWatermark,
                cancellationToken);

            var processed = 0;
            var skipped = 0;

            foreach (var row in batch.Rows)
            {
                if (!ShouldProcessRow(row))
                {
                    continue;
                }

                if (await HandleRowAsync(row, scope.ServiceProvider, cancellationToken))
                {
                    processed++;
                }
                else
                {
                    skipped++;
                }
            }

            // Monotonic advance is the reader's contract — the
            // batch's HighestSeenId is the SQL-ordered tail's id (the
            // .NET Guid-vs-PG uuid ordering never enters the loop),
            // and the SQL upsert inside the watermark store is the
            // single atomic statement that holds the contract on the
            // server side (GREATEST(...) never moves the watermark
            // backward).
            if (batch.HighestSeenId != previousWatermark)
            {
                await watermarks.RecordAsync(WatermarkKey, batch.HighestSeenId, cancellationToken);
                Logger.LogInformation(
                    "{Worker} processed {Processed}, skipped {Skipped} up to {LastSeenId}",
                    Name, processed, skipped, batch.HighestSeenId);
            }

            return WorkerResult.Ok(
                $"{Name}-read {batch.Rows.Count}, processed {processed}, skipped {skipped}",
                batch);
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or TimeoutException)
        {
            return WorkerResult.Fail($"{Name} cycle failed: {exception.Message}");
        }
    }

    /// <summary>Per-row gate — defaults to "type is in <see cref="GetSubscribedTypes"/>" but subscribers may layer additional filters (e.g. terminal-status check).</summary>
    protected virtual bool ShouldProcessRow(OrchestrationOutboxRow row)
    {
        return GetSubscribedTypes().Contains(row.Type);
    }

    /// <summary>Per-row work — returns true on a meaningful application-side effect (admit / cancel / ingest / launch), false on a poison-row skip.</summary>
    protected abstract Task<bool> HandleRowAsync(
        OrchestrationOutboxRow row,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}
