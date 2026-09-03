using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Tickets;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Intake.Application.Tickets;

/// <summary>
/// Native intake (<c>POST /api/v1/tickets</c>): creates the ticket and
/// its run in one motion — no admission rules, no sync-back (there is no
/// external tracker). Subject to the same one-live-run lock: a repeat
/// for the same external id while active answers a conflict.
/// </summary>
/// <param name="store"></param>
/// <param name="runLauncher"></param>
/// <param name="clock"></param>
/// <param name="validator"></param>
/// <param name="logger"></param>
public sealed class CreateNativeTicketHandler(
    IIntakeStore store,
    IRunLauncher runLauncher,
    TimeProvider clock,
    IValidator<CreateNativeTicketCommand> validator,
    ILogger<CreateNativeTicketHandler> logger)
{
    /// <summary>Creates the ticket and launches its run.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="IntakeTicketConflictException">An active ticket for the external id already exists.</exception>
    public async Task<IntakeTicketView> HandleAsync(CreateNativeTicketCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var now = clock.GetUtcNow();
        var externalId = command.ExternalId is { Length: > 0 } supplied
            ? supplied.Trim()
            : "native-" + Guid.NewGuid().ToString("N");

        var ticket = IncomingTicket.Create(
            command.ProjectId,
            TicketProvider.Native,
            externalId,
            command.Title.Trim(),
            command.Body,
            command.Author?.Trim() ?? "native",
            url: string.Empty,
            projectKey: null,
            labels: [],
            InboundTicketKind.Issue,
            now);

        var stored = await store.TryInsertTicketAsync(ticket, cancellationToken)
            ?? throw new IntakeTicketConflictException(ticket.Id, "an active ticket for this external id already exists");

        var runId = await runLauncher.LaunchAsync(command.ProjectId, null, stored, cancellationToken);
        await store.TryMarkClaimedAsync(stored.Id, runId, cancellationToken);
        logger.LogInformation("Native ticket {ExternalId} launched into run {RunId}", externalId, runId);

        return IntakeTicketView.Of(stored) with { Status = "Claimed", RunId = runId.Value };
    }
}
