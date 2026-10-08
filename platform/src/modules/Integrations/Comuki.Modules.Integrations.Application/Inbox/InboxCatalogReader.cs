using Comuki.Modules.Integrations.Application.Options;
using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Application.Sources;
using Comuki.Modules.Integrations.Application.Sync;
using Comuki.Modules.Integrations.Application.Views;
using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Integrations.Application.Inbox;

/// <summary>
/// The inbox read side: the local pending list (webhook-parked tickets)
/// and the live catalog page of an external connection (inbox mode's
/// "browse then take" view).
/// </summary>
/// <param name="store"></param>
/// <param name="providers"></param>
/// <param name="options"></param>
public sealed class InboxCatalogReader(
    IIntegrationsStore store,
    TicketProviderRegistry providers,
    IOptions<IntegrationsOptions> options)
{
    /// <summary>The pending tickets of the inbox, newest first.</summary>
    /// <param name="projectId">Optional project filter.</param>
    /// <param name="cancellationToken"></param>
    /// 
    public async Task<IReadOnlyList<InboundItemView>> ListPendingAsync(ProjectId? projectId, CancellationToken cancellationToken = default)
    {
        var pending = await store.ListPendingAsync(projectId, options.Value.InboxListLimit, cancellationToken);
        return [.. pending.Select(InboundItemView.Of)];
    }

    /// <summary>Fetches one page of a connection's external issue catalog.</summary>
    /// <param name="connectionId"></param>
    /// <param name="page">1-based page number.</param>
    /// <param name="cancellationToken"></param>
    /// 
    /// <exception cref="SourceConnectionNotFoundException">Unknown connection id.</exception>
    public async Task<IReadOnlyList<InboundItemView>> FetchCatalogAsync(
        SourceConnectionId connectionId,
        int page,
        CancellationToken cancellationToken = default)
    {
        var connection = await store.FindConnectionAsync(connectionId, cancellationToken)
            ?? throw new SourceConnectionNotFoundException(connectionId);

        var provider = providers.FindSource(TicketProviderKeys.Key(connection.Provider))
            ?? throw new SourceConnectionNotFoundException(connectionId);

        var tickets = await provider.FetchCatalogAsync(connection, Math.Max(page, 1), cancellationToken);
        return [.. tickets.Select(InboundItemView.Of)];
    }
}
