using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Identity.Domain.Scopes;

/// <summary>
/// The scope of a role assignment: platform (no project) or exactly one
/// project. Role and scope are different axes — one role on three projects
/// is three assignments, not three roles.
/// </summary>
/// <param name="Level"></param>
/// <param name="ProjectId">Present only when <see cref="Level"/> is <see cref="ScopeLevel.Project"/>.</param>
public readonly record struct AssignmentScope(ScopeLevel Level, ProjectId? ProjectId)
{
    /// <summary>Creates the platform scope.</summary>
    public static AssignmentScope Platform()
    {
        return new AssignmentScope(ScopeLevel.Platform, null);
    }

    /// <summary>Creates a project scope; the project id must be set.</summary>
    /// <param name="projectId"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">The project id is empty.</exception>
    public static AssignmentScope ForProject(ProjectId projectId)
    {
        return projectId.Value != Guid.Empty
            ? new AssignmentScope(ScopeLevel.Project, projectId)
            : throw new ArgumentException("project scope requires a project id", nameof(projectId));
    }
}
