namespace Comuki.Host.Projects.Models;

/// <summary>Wire body of PATCH /api/v1/projects/{projectId} — null fields are left untouched.</summary>
/// <param name="Name"></param>
/// <param name="Description"></param>
/// <param name="ProfilesGitUrl"></param>
/// <param name="ProfilesGitRef"></param>
public sealed record UpdateProjectRequest(
    string? Name,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef);
