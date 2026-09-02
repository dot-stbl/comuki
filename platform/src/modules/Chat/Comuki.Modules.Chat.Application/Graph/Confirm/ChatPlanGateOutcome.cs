using Comuki.Shared.Contracts.Plans;

namespace Comuki.Modules.Chat.Application.Graph.Confirm;

/// <summary>
/// Outcome of the plan gate: the validated plan and its canonical JSON.
/// </summary>
/// <param name="Plan">Validated plan; null when the gate rejected the brain output.</param>
/// <param name="CanonicalJson">Re-serialized plan JSON (valid output only).</param>
public sealed record ChatPlanGateOutcome(Plan? Plan, string CanonicalJson);
