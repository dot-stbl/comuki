using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Application.Tickets;

/// <summary>Creates a native ticket straight from the API surface (permission <c>run:create</c>).</summary>
/// <param name="ProjectId"></param>
/// <param name="Title"></param>
/// <param name="Body"></param>
/// <param name="ExternalId">Caller-supplied dedupe id; generated when empty.</param>
/// <param name="Author"></param>
public sealed record CreateNativeTicketCommand(
    ProjectId ProjectId,
    string Title,
    string Body,
    string? ExternalId,
    string? Author);
