using Comuki.Host.Realtime.Models;
using Comuki.Host.Realtime.Reading;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.SignalR;

namespace Comuki.Host.Realtime.Broadcasting;

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
    /// <summary>Client callback name of the run timeline stream.</summary>
    public const string RunEventMethod = "RunEvent";

    /// <summary>Client callback name of the project attention stream.</summary>
    public const string AttentionMethod = "Attention";

    /// <inheritdoc />
    public async Task BroadcastAsync(IReadOnlyList<RunEventEntry> entries, CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries)
        {
            await hubContext.Clients
                .Group(RealtimeGroups.RunGroup(entry.RunId))
                .SendAsync(RunEventMethod, RunEventViewMapping.ToView(entry), cancellationToken);
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
        var drafts = new List<(RunEventEntry Entry, AttentionDraft Draft)>();

        foreach (var entry in entries)
        {
            if (AttentionMap.FromEntry(entry) is { } draft)
            {
                drafts.Add((entry, draft));
            }
        }

        if (drafts.Count == 0)
        {
            return;
        }

        var projects = await ReadProjectsAsync(
            scopeFactory,
            [.. drafts.Select(static draft => draft.Entry.RunId).Distinct()],
            cancellationToken);

        foreach (var (entry, draft) in drafts)
        {
            if (!projects.TryGetValue(entry.RunId, out var project))
            {
                logger.LogWarning(
                    "Skipping attention broadcast for run {RunId}: owning project not found",
                    entry.RunId.Value);
                continue;
            }

            await hubContext.Clients
                .Group(RealtimeGroups.ProjectAttentionGroup(project))
                .SendAsync(
                    SignalRRunEventsBroadcaster.AttentionMethod,
                    new AttentionView(
                        entry.RunId.Value,
                        project.Value,
                        draft.WorkItemId,
                        draft.Status,
                        draft.AttentionKind,
                        entry.OccurredAt.ToUnixTimeMilliseconds()),
                    cancellationToken);
        }
    }

    /// <summary>One scoped batch read of run → project.</summary>
    private static async Task<IReadOnlyDictionary<RunId, ProjectId>> ReadProjectsAsync(
        IServiceScopeFactory scopeFactory,
        IReadOnlyCollection<RunId> runIds,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IRealtimeRunProjects>();

        return await reader.ReadAsync(runIds, cancellationToken);
    }
}
