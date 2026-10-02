namespace Comuki.Host.Projects.Models;

/// <summary>
/// Wire body of PATCH /api/v1/projects/{projectId} — null fields are left
/// untouched; an empty tags array clears the list. Icon and color follow
/// the same shapes as on create (≤ 200 chars / <c>#rrggbb</c>).
/// </summary>
public sealed record UpdateProjectRequest(
    string? Name,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef,
    string? Icon = null,
    string? Color = null,
    IReadOnlyList<string>? Tags = null,
    string? SourceGitUrl = null,
    string? SourceGitRef = null);
