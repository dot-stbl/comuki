using Comuki.Modules.Intake.Application.Admission;
using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Application.Sync;
using Comuki.Modules.Intake.Domain.Deliveries;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Tickets;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Intake.Application.Tickets;

/// <summary>
/// The webhook pipeline (scope-draft §1 "оба замка"): insert-first
/// delivery lock → signature verify → tolerant normalize → admission →
/// ticket upsert → run launch (watch mode). Every non-2xx path is
/// narrow: unknown provider/connection (404) and a bad signature (401);
/// everything else — replays, skips, filtered tickets, duplicates — is a
/// 200 with an outcome label, so trackers never retry letters we
/// deliberately dropped.
/// </summary>
/// <param name="store"></param>
/// <param name="providers"></param>
/// <param name="runLauncher"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class WebhookIntakeService(
    IIntakeStore store,
    TicketProviderRegistry providers,
    IRunLauncher runLauncher,
    TimeProvider clock,
    ILogger<WebhookIntakeService> logger)
{
    /// <summary>Runs one webhook delivery through the pipeline.</summary>
    /// <param name="sourceKey">Kebab-case provider key from the route.</param>
    /// <param name="webhookKey">Per-connection routing key from the route.</param>
    /// <param name="delivery"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<WebhookReceipt> HandleAsync(
        string sourceKey,
        string webhookKey,
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        if (sourceKey == TicketProviderKeys.Native)
        {
            return WebhookReceipt.NotFound(
                "intake.source_provider_not_found",
                $"source '{sourceKey}' has no webhook surface");
        }

        var provider = providers.FindSource(sourceKey);
        if (provider is null)
        {
            return WebhookReceipt.NotFound(
                "intake.source_provider_not_found",
                $"source '{sourceKey}' is not a registered ticket provider");
        }

        var connection = await store.FindConnectionByWebhookAsync(sourceKey, webhookKey, cancellationToken);
        if (connection is null)
        {
            return WebhookReceipt.NotFound(
                "intake.connection_not_found",
                $"no enabled connection for source '{sourceKey}' with this webhook key");
        }

        // Lock #1 — insert-first idempotency: the unique index on
        // (source, delivery_id) rejects the same letter twice.
        var deliveryRow = IntakeDelivery.Create(sourceKey, provider.DeliveryIdOf(delivery), clock.GetUtcNow());
        if (!await store.TryInsertDeliveryAsync(deliveryRow, cancellationToken))
        {
            logger.LogInformation("Webhook replay skipped for source {Source} delivery {DeliveryId}", sourceKey, deliveryRow.DeliveryId);
            return WebhookReceipt.Ok(DeliveryOutcomes.Replay, deliveryRow.DeliveryId);
        }

        // The signature IS the auth on this surface.
        if (!provider.VerifySignature(connection, delivery))
        {
            logger.LogWarning("Webhook signature rejected for source {Source} connection {ConnectionId}", sourceKey, connection.Id);
            await store.MarkDeliveryOutcomeAsync(deliveryRow.Id, DeliveryOutcomes.Rejected, "signature mismatch", cancellationToken);
            return WebhookReceipt.SignatureInvalid("webhook signature verification failed");
        }

        var ticket = provider.Normalize(delivery, connection.ProjectId);
        if (ticket is null)
        {
            logger.LogDebug("Webhook skipped for source {Source} delivery {DeliveryId}: not a ticket event", sourceKey, deliveryRow.DeliveryId);
            await store.MarkDeliveryOutcomeAsync(deliveryRow.Id, DeliveryOutcomes.Skipped, "not a ticket event", cancellationToken);
            return WebhookReceipt.Ok(DeliveryOutcomes.Skipped, deliveryRow.DeliveryId);
        }

        var rules = await store.ListEnabledRulesAsync(connection.ProjectId, cancellationToken);
        var admittedMode = AdmissionEvaluator.Evaluate(rules, ticket);

        if (admittedMode is null)
        {
            ticket.MarkDismissed(clock.GetUtcNow());
            await store.AddDismissedTicketAsync(ticket, cancellationToken);
            logger.LogInformation("Ticket {ExternalId} filtered out by admission rules of project {ProjectId}", ticket.ExternalId, ticket.ProjectId);
            await store.MarkDeliveryOutcomeAsync(deliveryRow.Id, DeliveryOutcomes.Filtered, ticket.ExternalId, cancellationToken);
            return WebhookReceipt.Ok(DeliveryOutcomes.Filtered, ticket.ExternalId);
        }

        // Lock #2 — one live run per issue: the partial unique index over
        // the active statuses rejects a second active ticket.
        var stored = await store.TryInsertTicketAsync(ticket, cancellationToken);
        if (stored is null)
        {
            logger.LogInformation("Ticket {ExternalId} skipped: an active ticket/run already exists in project {ProjectId}", ticket.ExternalId, ticket.ProjectId);
            await store.MarkDeliveryOutcomeAsync(deliveryRow.Id, DeliveryOutcomes.Duplicate, ticket.ExternalId, cancellationToken);
            return WebhookReceipt.Ok(DeliveryOutcomes.Duplicate, ticket.ExternalId);
        }

        if (admittedMode == AdmissionMode.Watch)
        {
            var runId = await runLauncher.LaunchAsync(connection.ProjectId, stored, cancellationToken);
            await store.TryMarkClaimedAsync(stored.Id, runId, cancellationToken);
            logger.LogInformation("Ticket {ExternalId} admitted into run {RunId} (watch)", stored.ExternalId, runId);
            await store.MarkDeliveryOutcomeAsync(deliveryRow.Id, DeliveryOutcomes.Admitted, stored.ExternalId, cancellationToken);
            return WebhookReceipt.Ok(DeliveryOutcomes.Admitted, stored.ExternalId);
        }

        logger.LogInformation("Ticket {ExternalId} parked in the inbox of project {ProjectId}", stored.ExternalId, stored.ProjectId);
        await store.MarkDeliveryOutcomeAsync(deliveryRow.Id, DeliveryOutcomes.Pending, stored.ExternalId, cancellationToken);
        return WebhookReceipt.Ok(DeliveryOutcomes.Pending, stored.ExternalId);
    }
}
