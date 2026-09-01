using System.Text.Json;
using Comuki.Shared.Contracts.Plans;
using FluentValidation;

namespace Comuki.Modules.Chat.Application.Graph;

/// <summary>
/// Brain plan JSON → validated canonical plan JSON (or an honest rejection):
/// the structural gate every plan-mode brain output passes through before
/// the approve card is shown.
/// </summary>
public static class ChatPlanGate
{
    /// <summary>Reply shown while the approve card is pending.</summary>
    public const string CardPrompt = "Here is the plan — approve it to queue the run, or reject with a reason.";

    /// <summary>Reply shown when the brain output failed the structural gate.</summary>
    public const string InvalidPlanMessage =
        "The brain returned a plan that failed structural validation (missing keys, unknown dependencies or a cycle). "
        + "Refine the request and try again.";

    /// <summary>Parses, validates and canonicalizes a brain plan payload.</summary>
    /// <param name="finalJson">Brain final payload of a plan invocation.</param>
    /// <param name="planValidator">Structural validator.</param>
    public static ChatPlanGateOutcome Validate(string finalJson, IValidator<Plan> planValidator)
    {
        return !ChatPlanParsing.TryDeserialize(finalJson, out var plan) || !planValidator.Validate(plan).IsValid
            ? new ChatPlanGateOutcome(null, string.Empty)
            : new ChatPlanGateOutcome(plan, JsonSerializer.Serialize(plan, JsonSerializerOptions.Web));
    }
}

file static class ChatPlanParsing
{
    public static bool TryDeserialize(string finalJson, out Plan plan)
    {
        try
        {
            // boundary: brain payload parsed with the shared Web options; a malformed payload is data, not an exception
            // (null parses to an empty plan, which the structural gate rejects)
            plan = JsonSerializer.Deserialize<Plan>(finalJson, JsonSerializerOptions.Web) ?? new Plan([]);
            return true;
        }
        catch (JsonException)
        {
            plan = new Plan([]);
            return false;
        }
    }
}
