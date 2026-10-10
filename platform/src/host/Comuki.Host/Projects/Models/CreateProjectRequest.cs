namespace Comuki.Host.Projects.Models;

/// <summary>
/// Wire body of POST /api/v1/projects. The slug is lower-case kebab-case
/// (3–64 chars) and becomes the immutable URL key — a duplicate gets HTTP
/// 409. Icon is an opaque emoji or image URL (≤ 200 chars); color is a
/// <c>#rrggbb</c> hex value stored lower-case; each tag is lower-case
/// kebab (1–39 chars), at most 20 distinct.
/// </summary>
public sealed record CreateProjectRequest(
    string Name,
    string Slug,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef,
    string? Icon = null,
    string? Color = null,
    IReadOnlyList<string>? Tags = null);
