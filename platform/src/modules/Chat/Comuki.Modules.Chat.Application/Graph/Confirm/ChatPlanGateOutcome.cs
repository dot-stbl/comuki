using Comuki.Shared.Contracts.Plans;

namespace Comuki.Modules.Chat.Application.Graph.Confirm;

/// <summary>
/// Outcome of the plan gate: the validated plan, its canonical JSON, and —
/// when the brain answered plan mode with prose instead of JSON — that
/// explanation, which the turn shows to the user verbatim.
/// </summary>
/// <param name="Plan">Validated plan; null when the gate rejected the brain output.</param>
/// <param name="CanonicalJson">Re-serialized plan JSON (valid output only).</param>
/// <param name="Explanation">The brain's own prose payload when it could not emit a plan (the invalid-plan fallback); empty otherwise.</param>
public sealed record ChatPlanGateOutcome(Plan? Plan, string CanonicalJson, string Explanation);
