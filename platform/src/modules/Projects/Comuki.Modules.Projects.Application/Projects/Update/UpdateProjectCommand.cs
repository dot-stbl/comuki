using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Projects.Update;

/// <summary>
/// Partial project update (PATCH semantics): a null field leaves the stored
/// value untouched. The slug is not editable — it is the stable external key.
/// The tags list is the one asymmetry (design D5): null keeps the stored
/// list, an empty list clears it.
/// </summary>
public sealed record UpdateProjectCommand(
    ProjectId ProjectId,
    string? Name,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef,
    string? Icon = null,
    string? Color = null,
    IReadOnlyList<string>? Tags = null);
