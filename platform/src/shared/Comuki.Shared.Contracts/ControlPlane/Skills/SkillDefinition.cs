namespace Comuki.Shared.Contracts.ControlPlane.Skills;

/// <summary>Catalog-facing skill metadata. The system-prompt body is deliberately not part of it.</summary>
/// <param name="Key">Stable identity: the directory name of the skill (e.g. <c>citation-cleanup</c>); used in plans and work items.</param>
/// <param name="Name">Human-readable name from the document frontmatter.</param>
/// <param name="Description">One-line description for catalogs and the brain skill-listing tool.</param>
/// <param name="Version">Semver from <c>version</c> frontmatter, or <c>0.1.0</c> when absent (task 25.4).</param>
/// <param name="TriggerWhen">Free-text hints from <c>trigger_when</c>, normalised to a list (empty when absent). The brain reads these; the catalog never narrows selection by them (task 25.5).</param>
/// <param name="ValidateAgainstPaths">Plain-string entries from <c>validate_against</c> (paths, URLs).</param>
/// <param name="ValidateAgainstRefs">SourceRef-shaped entries from <c>validate_against</c> (<c>{ kind, id }</c> objects).</param>
public sealed record SkillDefinition(
    string Key,
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> TriggerWhen,
    IReadOnlyList<string> ValidateAgainstPaths,
    IReadOnlyList<SkillValidateAgainstTarget> ValidateAgainstRefs);
