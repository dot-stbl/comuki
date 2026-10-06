namespace Comuki.Host.ControlPlane.Parsing;

/// <summary>
/// A parsed skill document: frontmatter metadata plus the markdown body.
/// <c>TriggerWhen</c> is the normalised list form (empty when absent);
/// <c>ValidateAgainstPaths</c> and <c>ValidateAgainstRefs</c> split the
/// mixed-shape <c>validate_against</c> by type so the brain can iterate each
/// list without re-parsing.
/// </summary>
/// <param name="Name">Document name (frontmatter <c>name</c>).</param>
/// <param name="Description">Short description (frontmatter <c>description</c>).</param>
/// <param name="Version">Skill semver (default <c>0.1.0</c>).</param>
/// <param name="TriggerWhen">Natural-language hints for when the skill applies.</param>
/// <param name="ValidateAgainstPaths">Plain string paths/URLs from <c>validate_against</c>.</param>
/// <param name="ValidateAgainstRefs">SourceRef-shaped objects from <c>validate_against</c>.</param>
/// <param name="Body">Markdown body after the frontmatter fence.</param>
public sealed record SkillDocument(
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> TriggerWhen,
    IReadOnlyList<string> ValidateAgainstPaths,
    IReadOnlyList<IReadOnlyDictionary<string, string>> ValidateAgainstRefs,
    string Body);
