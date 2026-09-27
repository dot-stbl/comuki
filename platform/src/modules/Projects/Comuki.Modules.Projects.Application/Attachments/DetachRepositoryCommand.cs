using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>Detaches a Repository from a Project.</summary>
/// <param name="ProjectId"></param>
/// <param name="RepositoryId"></param>
public sealed record DetachRepositoryCommand(
    ProjectId ProjectId,
    RepositoryId RepositoryId);
