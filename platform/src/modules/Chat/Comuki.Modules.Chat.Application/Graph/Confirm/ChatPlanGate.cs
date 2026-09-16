using Comuki.Shared.Contracts.Plans;

namespace Comuki.Modules.Chat.Application.Graph.Confirm;

/// <summary>
/// Brain plan JSON → validated canonical plan JSON (or an honest rejection):
/// the structural gate every plan-mode brain output passes through before
/// the approve card is shown. Delegates parse+validate to the shared
/// <see cref="PlanJson"/> bridge (one pass, frozen Web options).
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
    public static ChatPlanGateOutcome Validate(string finalJson)
    {
        if (PlanJson.TryParse(finalJson, out var plan, out _))
        {
            return new ChatPlanGateOutcome(plan, PlanJson.Serialize(plan), string.Empty);
        }

        // A payload that is not JSON at all is the brain's own explanation
        // (the invalid-plan fallback the brain host streams instead of
        // faulting) — the turn shows it to the user verbatim rather than
        // the generic rejection.
        return BrainPlanExplanation.IsExplanation(finalJson)
            ? new ChatPlanGateOutcome(null, string.Empty, finalJson)
            : new ChatPlanGateOutcome(null, string.Empty, string.Empty);
    }
}

/// <summary>Distinguishes the brain's prose explanations from JSON payloads.</summary>
file static class BrainPlanExplanation
{
    public static bool IsExplanation(string payload)
    {
        // a plan payload is always a JSON object or array; the fallback
        // prose never starts with one
        var trimmed = payload.TrimStart();
        return trimmed.Length > 0 && trimmed[0] is not ('{' or '[');
    }
}
