namespace Comuki.Modules.Identity.Domain.Scopes;

/// <summary>
/// The two scope levels of the Comuki model (scope-draft §10): platform
/// (everywhere) and project (one project). No deeper tree.
/// </summary>
public enum ScopeLevel
{
    /// <summary>Applies across the whole platform.</summary>
    Platform = 0,

    /// <summary>Applies to a single project.</summary>
    Project = 1,
}
