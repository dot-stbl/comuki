using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Views;

/// <summary>
/// Read model of a project — everything the operational UI needs, nothing
/// internal. Identity fields arrive display-ready: the colour is lower-case
/// <c>#rrggbb</c>, tags are trimmed/lower-cased/deduplicated, the icon is an
/// opaque string (emoji or URL) to render verbatim.
/// </summary>
public sealed record ProjectView
{
    public required ProjectId Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required string? Description { get; init; }

    public required string? ProfilesGitUrl { get; init; }

    public required string? ProfilesGitRef { get; init; }

    public required string? Icon { get; init; }

    public required string? Color { get; init; }

    public required string[] Tags { get; init; } = [];

    public required bool Archived { get; init; }

    public required DateTimeOffset? ArchivedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
