using System.Text.Json;
using Comuki.Shared.Contracts.Plans;

namespace Comuki.Host.Chat;

/// <summary>
/// create_ticket tool arguments (camelCase on the wire) — the same shape
/// the graph's act node serializes: project scope plus the canonical plan
/// JSON produced by the plan gate.
/// </summary>
/// <param name="ProjectId">Session project scope, guid string.</param>
/// <param name="PlanJson">Canonical approved plan JSON.</param>
internal sealed record ChatTicketArguments(string ProjectId, string PlanJson);

/// <summary>Parsing helpers over the tool argument JSON.</summary>
internal static class ChatTicketArgumentParsing
{
    /// <summary>Parses create_ticket arguments; null when the payload is malformed, the plan is missing or the project id is not a guid.</summary>
    /// <param name="argumentsJson">Tool arguments JSON.</param>
    public static ChatTicketArguments? Parse(string argumentsJson)
    {
        return JsonSerializer.Deserialize<ChatTicketArguments>(argumentsJson, JsonSerializerOptions.Web) is { } ticket
            && Guid.TryParse(ticket.ProjectId, out _)
            && !string.IsNullOrWhiteSpace(ticket.PlanJson)
            ? ticket
            : null;
    }

    /// <summary>Parses the canonical plan JSON; null when malformed.</summary>
    /// <param name="planJson">Canonical plan JSON from the gate.</param>
    public static Plan? ParsePlan(string planJson)
    {
        try
        {
            // boundary: graph-produced canonical JSON deserialized with the shared Web options
            return JsonSerializer.Deserialize<Plan>(planJson, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
