using System.Text.Json;
using Comuki.Shared.Contracts.Plans;

namespace Comuki.Host.Chat;

/// <summary>create_ticket tool arguments (camelCase on the wire); the plan nests the shared contract shape.</summary>
/// <param name="ProjectId">Session project scope, guid string.</param>
/// <param name="Plan">Approved plan.</param>
internal sealed record ChatTicketArguments(string ProjectId, Plan Plan);

/// <summary>Parsing helpers over the tool argument JSON.</summary>
internal static class ChatTicketArgumentParsing
{
    /// <summary>Parses create_ticket arguments; null when the payload is malformed or the project id is missing.</summary>
    /// <param name="argumentsJson">Tool arguments JSON.</param>
    public static ChatTicketArguments? Parse(string argumentsJson)
    {
        return JsonSerializer.Deserialize<ChatTicketArguments>(argumentsJson, JsonSerializerOptions.Web) is { } ticket
            && Guid.TryParse(ticket.ProjectId, out _)
            ? ticket
            : null;
    }
}
