using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Completion;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Shared.Contracts;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Admission;

/// <summary>
/// <c>Work.AdmitTask</c> command — admits a WorkTask idempotently on
/// an inbound id. A replayed admission (webhook replay or claim
/// retry) returns the same <see cref="WorkTaskId"/> — the
/// <c>integrations</c> → Work handoff uses the inbound-item external
/// id as the dedupe key. The Application layer is the only place
/// that emits Work outbox events; the host wires
/// <see cref="IOutbox"/> to the engine's durable outbox
/// (per design decision 2). The command body is the single
/// source of truth for admission idempotency.
/// </summary>
public sealed record AdmitTaskCommand(
    string InboundItemExternalId,
    ProjectId ProjectId,
    string Title,
    string Brief,
    WorkTaskSourceKind SourceKind,
    string SourceDisplayName);

/// <summary>
/// Outcome of an admission call — the WorkTaskId the inbound item
/// is now bound to. A replayed command returns the same
/// <c>TaskId</c> as the original admission; <see cref="WasReplay"/>
/// is true on replays, false on first admission.
/// </summary>
public sealed record AdmitTaskOutcome(WorkTaskId TaskId, bool WasReplay);

/// <summary>
/// Handler for <see cref="AdmitTaskCommand"/>. Idempotency: the
/// inbound-item external id is the dedupe key. The store keeps a
/// per-external-id record (a thin index on top of <c>WorkTask</c> +
/// <c>inbox_receipts</c> in the Work schema) so a replay returns
/// the same <c>TaskId</c> without creating a second Task. The
/// first admission publishes a <c>work.task.created.v1</c> event
/// with the same envelope id as the dedupe key, so a downstream
/// consumer that replays the inbox row no-ops. Replays do NOT
/// republish.
/// </summary>
public sealed class AdmitTaskHandler(
    IWorkTaskStore store,
    IWorkInbox inbox,
    IOutbox outbox,
    IInboundItemBindingStore bindings,
    TimeProvider clock,
    ILogger<AdmitTaskHandler> logger)
{
    /// <summary>The envelope / dedupe key the host stores per inbound id.</summary>
    public const string InboxMessagePrefix = "work.inbox.admit.";

    /// <summary>Builds the dedupe key for an inbound id.</summary>
    public static string InboxMessageId(string inboundItemExternalId)
    {
        return InboxMessagePrefix + inboundItemExternalId;
    }

    /// <summary>Wire-format payload the handler publishes on first admission.</summary>
    public sealed record AdmittedEvent(Guid TaskId, string InboundItemExternalId);

    /// <summary>Handles the command. Idempotent on <paramref name="command.InboundItemExternalId"/>.</summary>
    public async Task<AdmitTaskOutcome> HandleAsync(AdmitTaskCommand command, CancellationToken cancellationToken = default)
    {
        var messageId = InboxMessageId(command.InboundItemExternalId);
        if (!await inbox.TryClaimAsync(messageId, cancellationToken))
        {
            // Replay: the inbox receipt already exists. The
            // binding is the authoritative source for the original
            // Task id; load it and return. No outbox event is
            // published on replay.
            var existing = await bindings.FindByInboundAsync(command.InboundItemExternalId, cancellationToken)
                ?? throw new WorkTaskDomainException(
                    WorkTaskErrorCodes.InboxBindingMissing,
                    $"inbox claimed '{messageId}' but no inbound binding row exists; data is inconsistent");
            logger.LogInformation(
                "AdmitTask replay for inbound {InboundId}; existing Task {TaskId}",
                command.InboundItemExternalId, existing.Value);
            return new AdmitTaskOutcome(existing, WasReplay: true);
        }

        // First admission — claim won; build the Task, persist,
        // publish the event. The dual-write below is a known
        // architectural limitation, not an atomicity claim: the
        // Application layer's IOutbox seam is host-bridged to the
        // engine's `orchestration.outbox_messages` table via a
        // separate transaction (see `add-work-management/design.md`
        // Open Question A's mirror-table recommendation). A crash
        // between the store SaveChanges and the outbox publish
        // leaves the WorkTask row written without the
        // `work.task.created.v1` event — the Work-side dedupe
        // ledger (inbox_receipts) closes the replay path on the
        // next admission, not the atomic-one-shot path.
        var now = clock.GetUtcNow();
        var primary = WorkTaskSourceRef.Primary(
            command.SourceKind,
            command.InboundItemExternalId,
            command.SourceDisplayName);
        var task = WorkTask.Create(
            command.ProjectId,
            command.Title,
            command.Brief,
            primary,
            WorkTaskCompletionPolicy.Default(now),
            now);

        await store.SaveAsync(task, cancellationToken);
        await bindings.BindAsync(command.InboundItemExternalId, task.Id, cancellationToken);

        var payload = new AdmittedEvent(task.Id.Value, command.InboundItemExternalId);
        await outbox.PublishAsync(WorkTaskEventTypes.Created, payload, cancellationToken);

        logger.LogInformation(
            "AdmitTask accepted inbound {InboundId} as Task {TaskId}",
            command.InboundItemExternalId, task.Id);
        return new AdmitTaskOutcome(task.Id, WasReplay: false);
    }
}

/// <summary>
/// Per-inbound-item binding (inbound external id → WorkTaskId). The
/// port lives here so the handler doesn't depend on the engine
/// store. The Infrastructure project supplies the EF
/// implementation; the in-memory fake lives in
/// <c>Comuki.Modules.Work.Unit</c>.
/// </summary>
public interface IInboundItemBindingStore
{
    /// <summary>Resolves the inbound-id to the bound Task id, or null when no binding exists.</summary>
    public Task<WorkTaskId?> FindByInboundAsync(string inboundItemExternalId, CancellationToken cancellationToken = default);

    /// <summary>Binds the inbound id to the supplied Task id (one row per inbound id).</summary>
    public Task BindAsync(string inboundItemExternalId, WorkTaskId taskId, CancellationToken cancellationToken = default);
}
