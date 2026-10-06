namespace Comuki.Shared.Contracts.ControlPlane.Skills;

/// <summary>
/// One <c>validate_against</c> entry shaped like a Context Fabric <c>SourceRef</c>:
/// <c>kind</c> (e.g. <c>knowledge</c>, <c>control</c>) plus the ref id
/// (e.g. <c>doc/citation-style@v3</c>). The catalog never resolves the ref
/// itself; the brain interprets the kind and id when the skill is selected.
/// </summary>
/// <param name="Kind">SourceRef kind.</param>
/// <param name="Id">SourceRef id, scoped to the kind.</param>
public sealed record SkillValidateAgainstTarget(string Kind, string Id);
