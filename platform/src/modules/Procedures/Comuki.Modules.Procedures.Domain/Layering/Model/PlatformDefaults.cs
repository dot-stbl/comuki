namespace Comuki.Modules.Procedures.Domain.Layering.Model;

/// <summary>
/// The platform's default floors for every procedure on the platform
/// (design decision 2). Project policy may tighten these floors; a
/// repository binding never overrides them — a binding only selects
/// which procedure the repo executes against, never widens it.
/// 
/// <para>
/// Two procedures on the same platform share the platform's
/// <see cref="AllowedKindKeys"/>. A project that needs a kind outside
/// this set must publish a kind descriptor (task 1.1) and add it to
/// the platform's catalog; the layering never adds kinds itself.
/// </para>
/// </summary>
/// <param name="AllowedKindKeys">
/// The closed set of kind keys the procedure layer may instance. Any
/// <see cref="Definitions.Elements.ProcedureNode"/> outside this set is a
/// layering refusal (spec scenario: "Unknown node kind is rejected").
/// </param>
/// <param name="MaxGenerations">
/// Floor on repair-boundary generations. The compile gate (task 2.3)
/// rejects any procedure whose repair-boundary node declares a higher
/// generation cap.
/// </param>
/// <param name="MinApprovals">
/// Floor on human-gate approvals. The compile gate rejects any
/// human-gate that declares a lower approval count (the gate
/// integration, task 5.2, enforces the actual human approvals).
/// </param>
public sealed record PlatformDefaults(
    IReadOnlySet<string> AllowedKindKeys,
    int MaxGenerations,
    int MinApprovals);
