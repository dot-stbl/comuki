using Comuki.Host.Realtime.Models;
using Comuki.Host.Realtime.Reading;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Contracts.Realtime;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.SignalR;

namespace Comuki.Host.Realtime.Broadcasting;

/// <summary>
/// Paired entry + attention-draft record the attention helper iterates after
/// filtering. Named instead of an inline <c>(RunEventEntry, AttentionDraft)</c>
/// tuple so the call sites read <c>draft.Entry</c> / <c>draft.Draft</c>
/// rather than <c>draft.Item1</c> / <c>draft.Item2</c> and so the type
/// survives the rename of either side without a cascading parameter-order
/// change at every call site.
/// </summary>
/// <param name="Entry">Source <see cref="RunEventEntry"/> the draft was derived from.</param>
/// <param name="Draft">Resolved attention target — only set when the entry is attention-worthy.</param>
file sealed record RunEventAttentionDraft(RunEventEntry Entry, AttentionDraft Draft);

/// <summary>
/// Default <see cref="IRunEventsBroadcaster"/> over the
/// <see cref="IHubContext{RunsHub}"/>: one message per entry to its run
/// group, plus attention signals to the owning project groups. The
/// run→project lookup runs in its own DI scope (the broadcaster is a
/// singleton, the reader is scoped over a DbContext); a run the lookup
/// cannot resolve yields its run-group event but skips the attention
/// signal — logged, never thrown.
/// </summary>
/// <param name="hubContext">Hub context without a live connection.</param>
/// <param name="scopeFactory">Scope for the per-batch project lookup.</param>
/// <param name="logger"></param>
public sealed class SignalRRunEventsBroadcaster(
    IHubContext<RunsHub> hubContext,
    IServiceScopeFactory scopeFactory,
    ILogger<SignalRRunEventsBroadcaster> logger) : IRunEventsBroadcaster
{
    /// <inheritdoc />
    public async Task BroadcastAsync(IReadOnlyList<RunEventEntry> entries, CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries)
        {
            await hubContext.Clients
                .Group(RealtimeGroups.RunGroup(entry.RunId))
                .SendAsync(RealtimeTransportMethods.RunEvent, RunEventViewMapping.ToView(entry), cancellationToken);
        }

        await SignalRRunEventsAttention.SendAsync(hubContext, scopeFactory, logger, entries, cancellationToken);
    }
}

/// <summary>
/// Attention half of the broadcast: filters the attention-worthy entries,
/// resolves their owning projects in one scoped batch read, and addresses
/// the project groups. Unresolvable runs skip their attention signal.
/// </summary>
file static class SignalRRunEventsAttention
{
    public static async Task SendAsync(
        IHubContext<RunsHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        IReadOnlyList<RunEventEntry> entries,
        CancellationToken cancellationToken)
    {
        var drafts = new List<RunEventAttentionDraft>();

        foreach (var entry in entries)
        {
            if (AttentionMap.FromEntry(entry) is { } draft)
            {
                drafts.Add(new RunEventAttentionDraft(entry, draft));
            }
        }

        if (drafts.Count == 0)
        {
            return;
        }

        var projects = await SignalRRunEventsBroadcastHelpers.ReadProjectsAsync(
            scopeFactory,
            [.. drafts.Select(static draft => draft.Entry.RunId).Distinct()],
            cancellationToken);

        foreach (var draft in drafts)
        {
            if (!projects.TryGetValue(draft.Entry.RunId, out var project))
            {
                logger.LogWarning(
                    "Skipping attention broadcast for run {RunId}: owning project not found",
                    draft.Entry.RunId.Value);
                continue;
            }

            await hubContext.Clients
                .Group(RealtimeGroups.ProjectAttentionGroup(project))
                .SendAsync(
                    RealtimeTransportMethods.Attention,
                    new AttentionView(
                        draft.Entry.RunId.Value,
                        project.Value,
                        draft.Draft.WorkItemId,
                        draft.Draft.Status,
                        draft.Draft.AttentionKind,
                        draft.Entry.OccurredAt.ToUnixTimeMilliseconds()),
                    cancellationToken);
        }
    }

    // ReadProjectsAsync lives in SignalRRunEventsBroadcastHelpers below —
    // this broadcaster orchestrator stays free of private static methods
    // per the no-private-methods rule.
}

/// <summary>
/// Per-call scope opener for <see cref="SignalRRunEventsBroadcaster"/>.
/// The broadcaster runs outside an HTTP context (BackgroundService); the
/// IRealtimeRunProjects port carries its own DI scope, which the
/// helper opens once per batch and disposes on return.
/// </summary>
file static class SignalRRunEventsBroadcastHelpers
{
    /// <summary>Opens one DI scope, reads run → project ids, returns.</summary>
    /// <param name="scopeFactory">Root <see cref="IServiceScopeFactory"/>.</param>
    /// <param name="runIds">Distinct run ids from the batch.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static async Task<IReadOnlyDictionary<RunId, ProjectId>> ReadProjectsAsync(
        IServiceScopeFactory scopeFactory,
        IReadOnlyCollection<RunId> runIds,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IRealtimeRunProjects>();

        return await reader.ReadAsync(runIds, cancellationToken);
    }
}
