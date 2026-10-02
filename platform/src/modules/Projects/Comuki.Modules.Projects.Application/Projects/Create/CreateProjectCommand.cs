namespace Comuki.Modules.Projects.Application.Projects.Create;

/// <summary>
/// Creates a project together with its default settings row. The slug is
/// stored trimmed and lower-cased — a duplicate is refused with a conflict.
/// The icon is an opaque display string (emoji or image URL, at most 200
/// characters); the colour is <c>#rrggbb</c> stored lower-case; tags are
/// normalized (trimmed, lower-cased, deduplicated, at most 20 distinct).
/// <c>EnvClass</c> is the project stand-in for the scalar source
/// repository's catalog binding (add-worker-environments task 2.2);
/// omit on create to leave the project without a class (implement
/// work items stay unclaimable).
/// </summary>
public sealed record CreateProjectCommand(
    string Name,
    string Slug,
    string? Description,
    string? ProfilesGitUrl,
    string? ProfilesGitRef,
    string? Icon = null,
    string? Color = null,
    IReadOnlyList<string>? Tags = null,
    string? EnvClass = null,
    string? SourceGitUrl = null,
    string? SourceGitRef = null);
