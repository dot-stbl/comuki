using Comuki.Shared.Contracts.Integrations;

namespace Comuki.Modules.Integrations.Application.Events;

/// <summary>
/// Internal aliases for the Integration-emitted wire event types.
/// The contract names themselves live in
/// <see cref="IntegrationWireEventTypes"/> so the publisher and
/// Work-side consumer share one source of truth. The aliases keep
/// the call sites in this module short (the original
/// <c>IntegrationEventTypes.InboundAdmittedV1</c> shape).
/// </summary>
public static class IntegrationEventTypes
{
    /// <summary>
    /// Emitted by <c>ClaimInboundItemHandler</c> when an
    /// InboundItem is admitted and launches a Run. The Work-side
    /// admission hook (<c>WorkAdmissionSubscriber</c>) consumes
    /// this and dispatches <c>Work.AdmitTask</c> idempotently
    /// (dedupe on <c>work.inbox.admit.{externalId}</c>, mirrored to
    /// <c>work.inbox_receipts</c>). The payload shape lives on
    /// <see cref="IntegrationInboundAdmittedEvent"/>.
    /// </summary>
    public const string InboundAdmittedV1 = IntegrationWireEventTypes.InboundAdmittedV1;
}
