namespace Comuki.Shared.Contracts.ControlPlane.Profiles;

/// <summary>Catalog-facing profile metadata. The system-prompt body is deliberately not part of it.</summary>
/// <param name="Key">Stable identity: the file stem of the profile document (e.g. <c>explore-readonly</c>); used in plans and work items.</param>
/// <param name="Name">Human-readable name from the document frontmatter.</param>
/// <param name="Description">What the profile is for; shown in the dashboard and the brain catalog tool.</param>
/// <param name="AllowedTools">Tool names the profile's workers may use (e.g. Read, Grep, Bash). Empty when the document does not restrict tools.</param>
/// <param name="Model">Optional model role hint (e.g. light/heavy) for routing; advisory, not a contract.</param>
public sealed record ProfileDefinition(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<string> AllowedTools,
    string? Model);
