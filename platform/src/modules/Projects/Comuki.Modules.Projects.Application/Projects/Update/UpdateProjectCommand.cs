using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Projects.Update;

/// <summary>
/// Partial project update (PATCH semantics): a null field leaves the stored
/// value untouched. The slug is not editable — it is the stable external key.
/// </summary>
/// <param name="ProjectId"></param>
/// <param name="Name"></param>
/// <param name="Description"></param>
/// <param name="ProfilesGitUrl"></param>
/// <param name="ProfilesGitRef"></param>
public sealed record UpdateProjectCommand(
    ProjectId ProjectId,
    string? Name,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef);
