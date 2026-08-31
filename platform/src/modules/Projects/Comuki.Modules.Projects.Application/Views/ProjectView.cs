using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Views;

/// <summary>Read model of a project — everything the operational UI needs, nothing internal.</summary>
/// <param name="Id"></param>
/// <param name="Name"></param>
/// <param name="Slug"></param>
/// <param name="Description"></param>
/// <param name="ProfilesGitUrl"></param>
/// <param name="ProfilesGitRef"></param>
/// <param name="Archived"></param>
/// <param name="ArchivedAt"></param>
/// <param name="CreatedAt"></param>
/// <param name="UpdatedAt"></param>
public sealed record ProjectView(
    ProjectId Id,
    string Name,
    string Slug,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef,
    bool Archived,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
