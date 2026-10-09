using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Modules.Work.Application.Ports.InboundItems;

namespace Comuki.Host.Work;

/// <summary>
/// Host-side adapter that closes <see cref="IInboundItemReader"/>
/// over <see cref="IIntegrationsStore"/>. The Work module never
/// sees the Integrations domain types — this adapter is the only
/// place that translates <c>InboundItem</c> ↔
/// <see cref="InboundItemSnapshot"/>; the Integrations DB context
/// is registered scoped in the host's DI graph and disposed by the
/// per-cycle scope the subscriber creates.
/// </summary>
public sealed class HostInboundItemReader(IIntegrationsStore integrationsStore) : IInboundItemReader
{
    /// <inheritdoc />
    public async Task<InboundItemSnapshot?> FindAsync(string inboundItemId, CancellationToken cancellationToken = default)
    {
        return Guid.TryParse(inboundItemId, out var guid)
            && await integrationsStore.FindTicketAsync(new InboundItemId(guid), cancellationToken) is { } ticket
            ? new InboundItemSnapshot(
                ExternalId: ticket.ExternalId,
                Title: ticket.Title,
                Body: ticket.Body,
                ProviderWire: ticket.Provider.ToString())
            : null;
    }
}
