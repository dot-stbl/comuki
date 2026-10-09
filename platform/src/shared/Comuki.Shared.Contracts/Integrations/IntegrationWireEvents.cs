namespace Comuki.Shared.Contracts.Integrations;

/// <summary>
/// Stable contract names for Integrations-emitted events that
/// cross the Integrations ↔ Work boundary on the engine outbox.
/// Both publisher (Integrations.Application) and consumer
/// (Work.Infrastructure) reference these strings from this
/// assembly so the wire contract has one source of truth — a
/// future change to the type name lands here, and the rest of
/// the project picks up the new string on its next build.
/// </summary>
public static class IntegrationWireEventTypes
{
    /// <summary>
    /// Emitted by <c>ClaimInboundItemHandler</c> when an
    /// InboundItem is admitted and launches a Run. The Work-side
    /// admission hook subscribes to this and dispatches
    /// <c>Work.AdmitTask</c> idempotently (dedupe on
    /// <c>work.inbox.admit.{externalId}</c>, mirrored to
    /// <c>work.inbox_receipts</c>). The payload shape lives on
    /// <see cref="IntegrationInboundAdmittedEvent"/>.
    /// </summary>
    public const string InboundAdmittedV1 = "integration.inbound.admitted.v1";
}

/// <summary>
/// Wire-format envelope for
/// <see cref="IntegrationWireEventTypes.InboundAdmittedV1"/> —
/// minimal carrier so the Work subscriber can find (or create) the
/// WorkTask bound to the inbound id. The fields are camelCase to
/// match the JSON wire form (System.Text.Json default). The Work
/// subscriber dedupes on <c>inboundItemId</c> via the Work-side
/// mirror <c>inbox_receipts</c> — no version field needed at the
/// wire layer because the publisher is the same process that
/// wrote the inbound; a duplicate emit at the publisher is
/// impossible.
/// </summary>
public sealed record IntegrationInboundAdmittedEvent(
    string InboundItemId,
    string ProjectId,
    DateTimeOffset AdmittedAt);
