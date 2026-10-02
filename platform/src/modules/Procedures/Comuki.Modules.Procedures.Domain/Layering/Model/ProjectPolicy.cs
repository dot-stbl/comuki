namespace Comuki.Modules.Procedures.Domain.Layering.Model;

/// <summary>
/// One project's contribution to a layered procedure (design decision
/// 2): the project's own graph plus optional floor tightening and
/// optional additional kinds the project declares. The procedure
/// graph itself comes from the project's <see cref="Definitions.ProcedureDefinition"/>;
/// the layering never mutates it.
/// 
/// <para>
/// <see cref="AdditionalAllowedKindKeys"/> is the merged-set union on
/// top of the platform's <see cref="PlatformDefaults.AllowedKindKeys"/> —
/// the platform never grants, the project may extend (with a kind the
/// platform already publishes in its catalog). Removing kinds that the
/// platform grants is impossible — the project's graph may simply not
/// instance them. Tightening floors is always allowed; loosening is
/// silently dropped (project-floor flags are ignored when the project
/// passes <c>null</c>).
/// </para>
/// </summary>
/// <param name="Definition">The procedure graph the project declares.</param>
/// <param name="AdditionalAllowedKindKeys">
/// Optional kinds the project needs on top of the platform's set.
/// Null means "no additions". An empty set means "no additions"
/// (an explicit empty allowlist narrows to nothing — interpreted the
/// same as null).
/// </param>
/// <param name="MaxGenerationsOverride">
/// Optional tighter repair-boundary generations floor. Null means
/// "use the platform's floor". A higher value than the platform's
/// floor is impossible — the compile gate (task 2.3) refuses.
/// </param>
/// <param name="MinApprovalsOverride">
/// Optional tighter human-gate approval floor. Null means "use the
/// platform's floor". A lower value than the platform's floor is
/// impossible.
/// </param>
public sealed record ProjectPolicy(
    Definitions.ProcedureDefinition Definition,
    IReadOnlySet<string>? AdditionalAllowedKindKeys = null,
    int? MaxGenerationsOverride = null,
    int? MinApprovalsOverride = null);
