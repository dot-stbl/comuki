using Comuki.Modules.Work.Domain.Sources;

namespace Comuki.Modules.Work.Application.Ports.InboundItems;

/// <summary>
/// Read-only projection the Work-side admission subscriber needs
/// from the Integrations module to convert an
/// <c>integration.inbound.admitted.v1</c> payload into the
/// <see cref="Admission.AdmitTaskCommand"/> shape. The Work module
/// never imports <c>Comuki.Modules.Integrations</c> directly — the
/// host wires this port to <c>IIntegrationsStore.FindTicketAsync</c>
/// (per <c>add-work-management</c> design §"Layer discipline":
/// cross-module reads go through Application ports, never through
/// concrete <c>IntegrationsDbContext</c> calls). The snapshot
/// carries only the fields the Work aggregate needs to admit the
/// Task — no <c>Provider</c> smart-type, no <c>Status</c>, no
/// connection / run / labels, all of which would re-couple the
/// Work bounded context to Integrations' Domain shape.
/// </summary>
public interface IInboundItemReader
{
    /// <summary>
    /// Looks up the inbound item by its <paramref name="inboundItemId"/>
    /// (UUIDv7 string form). Returns <c>null</c> when the row no
    /// longer exists (deleted between the publisher's claim and the
    /// Work subscriber's poll — the subscriber advances its watermark
    /// regardless).
    /// </summary>
    public Task<InboundItemSnapshot?> FindAsync(string inboundItemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Projection the Work admission subscriber needs from an
/// Integrations inbound item. Plain record
/// (not a smart-type) — the data crosses the Integrations ↔ Work
/// boundary as a wire snapshot, and the Integrations domain type
/// would couple the Work module to its sibling's internals. The
/// provider is the wire-form string the Integrations store persists
/// (e.g. <c>"GitHub"</c>, <c>"GitLab"</c>); the subscriber resolves
/// it via <see cref="WorkTaskSourceKind.FromWire"/>.
/// </summary>
public sealed record InboundItemSnapshot(string ExternalId, string Title, string Body, string ProviderWire);
