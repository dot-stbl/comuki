namespace Comuki.Modules.Projects.Application.Projects.Create;

/// <summary>Creates a project together with its default settings row.</summary>
/// <param name="Name"></param>
/// <param name="Slug"></param>
/// <param name="Description"></param>
/// <param name="ProfilesGitUrl"></param>
/// <param name="ProfilesGitRef"></param>
public sealed record CreateProjectCommand(
    string Name,
    string Slug,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef);
