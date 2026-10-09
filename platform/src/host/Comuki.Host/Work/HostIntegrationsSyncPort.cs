using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Modules.Integrations.Domain.Sync;
using Comuki.Modules.Work.Application.Ports.Integrations;

namespace Comuki.Host.Work;

/// <summary>
/// Host-side adapter that closes <see cref="IIntegrationsSyncPort"/>
/// over the Integrations module's <see cref="IIntegrationsStore"/>.
/// The Work module never references the Integrations types — this
/// adapter is the only place that translates a Work-side terminal
/// event into the Integrations
/// <see cref="SyncJob.Create"/> shape. The port carries
/// primitives (no Work domain types, no Integrations domain
/// types); a future Work-emitted integrations event slot is
/// additive on the port side.
/// </summary>
public sealed class HostIntegrationsSyncPort(IIntegrationsStore integrationsStore) : IIntegrationsSyncPort
{
    /// <inheritdoc />
    public async Task EnqueueTerminalSyncAsync(
        Guid taskId,
        string? primarySourceExternalId,
        string statusForJob,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        // The primary source's external id drives both the
        // ticketExternalId and the inbound id so they stay in
        // lock-step (a primary source either wins for both or both
        // fall back to the taskId). A native Task (no primary
        // source) keeps the taskId as both fields so the ticket
        // external id is never the same as the Work task id by
        // accident.
        var ticketExternalId = primarySourceExternalId ?? taskId.ToString();

        var resolvedInboundId = primarySourceExternalId is { } externalId
            && Guid.TryParse(externalId, out var inboundGuid)
            ? new InboundItemId(inboundGuid)
            : new InboundItemId(taskId);

        // WorkTaskId is the natural SourceConnectionId analogue;
        // the sync job's connection scope points at the WorkTask
        // that produced the lifecycle event, not the engine Run.
        var sourceConnectionId = new SourceConnectionId(taskId);
        var runId = new Shared.Kernel.Ids.RunId(taskId);

        await integrationsStore.EnqueueSyncJobAsync(
            SyncJob.Create(
                resolvedInboundId,
                sourceConnectionId,
                runId,
                ticketExternalId,
                $"work/tasks/{taskId}",
                statusForJob,
                occurredAt),
            cancellationToken);
    }
}
