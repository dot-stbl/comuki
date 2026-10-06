namespace Comuki.Shared.Kernel;

/// <summary>
/// Result of <see cref="YamlishFrontmatter.Extract"/>: the raw YAML-ish
/// frontmatter text and the body that follows the closing fence.
/// </summary>
public sealed record YamlishFrontmatterBlock(string Yaml, string Body);
