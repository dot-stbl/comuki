namespace Comuki.Host.Projects.Models;

/// <summary>Wire body of POST /api/v1/projects.</summary>
/// <param name="Name"></param>
/// <param name="Slug"></param>
/// <param name="Description"></param>
/// <param name="ProfilesGitUrl"></param>
/// <param name="ProfilesGitRef"></param>
public sealed record CreateProjectRequest(
    string Name,
    string Slug,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef);
