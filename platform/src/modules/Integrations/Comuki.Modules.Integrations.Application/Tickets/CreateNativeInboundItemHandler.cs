using Comuki.Modules.Integrations.Application.Ports.Admission;
using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Application.Views;
using Comuki.Modules.Integrations.Domain.Items;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Integrations.Application.Tickets;

/// <summary>
/// Native integrations item (<c>POST /api/v1/integration/items</c>): creates the ticket and
/// its run in one motion — no admission rules, no sync-back (there is no
/// external tracker). Subject to the same one-live-run lock: a repeat
/// for the same external id while active answers a conflict.
/// </summary>
/// <param name="store"></param>
/// <param name="runLauncher"></param>
/// <param name="clock"></param>
/// <param name="validator"></param>
/// <param name="logger"></param>
public sealed class CreateNativeInboundItemHandler(
    IIntegrationsStore store,
    IRunLauncher runLauncher,
    TimeProvider clock,
    IValidator<CreateNativeInboundItemCommand> validator,
    ILogger<CreateNativeInboundItemHandler> logger)
{
    /// <summary>Creates the ticket and launches its run.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// 
    /// <exception cref="InboundItemConflictException">An active ticket for the external id already exists.</exception>
    public async Task<InboundItemView> HandleAsync(CreateNativeInboundItemCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var now = clock.GetUtcNow();
        var externalId = command.ExternalId is { Length: > 0 } supplied
            ? supplied.Trim()
            : "native-" + Guid.NewGuid().ToString("N");

        var ticket = InboundItem.Create(
            command.ProjectId,
            TicketProvider.Native,
            externalId,
            command.Title.Trim(),
            command.Body,
            command.Author?.Trim() ?? "native",
            url: string.Empty,
            projectKey: null,
            labels: [],
            InboundItemKind.Issue,
            now);

        var stored = await store.TryInsertTicketAsync(ticket, cancellationToken)
            ?? throw new InboundItemConflictException(ticket.Id, "an active ticket for this external id already exists");

        var runId = await runLauncher.LaunchAsync(command.ProjectId, null, stored, cancellationToken);
        await store.TryMarkClaimedAsync(stored.Id, runId, cancellationToken);
        logger.LogInformation("Native ticket {ExternalId} launched into run {RunId}", externalId, runId);

        return InboundItemView.Of(stored) with { Status = nameof(InboundItemStatus.Claimed), RunId = runId.Value };
    }
}
