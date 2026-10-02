namespace Comuki.Modules.Procedures.Domain.Layering.Results;

/// <summary>
/// The effective repair-boundary and human-gate floors a layered
/// procedure runs with: the platform's defaults tightened by the
/// project's overrides. Replaces the ad-hoc two-int tuple the merge
/// used to return — the pair travels as one named value now.
/// </summary>
/// <param name="MaxGenerations">Effective repair-boundary generations floor.</param>
/// <param name="MinApprovals">Effective human-gate approval floor.</param>
public sealed record ApprovalFloors(int MaxGenerations, int MinApprovals);
