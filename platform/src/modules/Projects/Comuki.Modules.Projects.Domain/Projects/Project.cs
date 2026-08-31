using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Domain.Projects;

/// <summary>
/// A Comuki project — the scope unit for runs, work items, settings and
/// role assignments. <see cref="Slug"/> is the unique, immutable,
/// URL-facing key (created with the project, never renamed); archiving is
/// soft: the row stays for history while lists skip it by default.
/// </summary>
public sealed class Project
{
    internal Project()
    {
    }

    /// <summary>Strong-typed project id (UUIDv7, from the Shared Kernel).</summary>
    public ProjectId Id { get; private set; }

    /// <summary>Human-readable name shown in the UI.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Unique, immutable, lower-cased URL key; unique index in the database.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Free-form description; optional.</summary>
    public string? Description { get; private set; }

    /// <summary>Git URL of the client's worker profiles repository; optional.</summary>
    public string? ProfilesGitUrl { get; private set; }

    /// <summary>Pinned git ref of the profiles repository (branch, tag or digest).</summary>
    public string? ProfilesGitRef { get; private set; }

    /// <summary>Soft-archive flag; archived projects keep their runs and settings.</summary>
    public bool Archived { get; private set; }

    /// <summary>When the project was archived; null while active.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>When the project was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a project; the slug is normalized here, uniqueness is backed by the index.</summary>
    /// <param name="name"></param>
    /// <param name="slug"></param>
    /// <param name="description"></param>
    /// <param name="profilesGitUrl"></param>
    /// <param name="profilesGitRef"></param>
    /// <param name="now"></param>
    public static Project Create(
        string name,
        string slug,
        string? description,
        string? profilesGitUrl,
        string? profilesGitRef,
        DateTimeOffset now)
    {
        return new Project
        {
            Id = ProjectId.New(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description,
            ProfilesGitUrl = profilesGitUrl,
            ProfilesGitRef = profilesGitRef,
            Archived = false,
            ArchivedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Partial update: a null field leaves the stored value untouched (PATCH
    /// semantics). The slug is deliberately not editable — it is the stable
    /// external key other modules reference.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="description"></param>
    /// <param name="profilesGitUrl"></param>
    /// <param name="profilesGitRef"></param>
    /// <param name="now"></param>
    public void Update(
        string? name,
        string? description,
        string? profilesGitUrl,
        string? profilesGitRef,
        DateTimeOffset now)
    {
        if (name is { } nextName)
        {
            Name = nextName.Trim();
        }

        if (description is { } nextDescription)
        {
            Description = nextDescription;
        }

        if (profilesGitUrl is { } nextUrl)
        {
            ProfilesGitUrl = nextUrl;
        }

        if (profilesGitRef is { } nextRef)
        {
            ProfilesGitRef = nextRef;
        }

        UpdatedAt = now;
    }

    /// <summary>Soft-archives the project; archiving twice is a no-op.</summary>
    /// <param name="now"></param>
    public void Archive(DateTimeOffset now)
    {
        if (Archived)
        {
            return;
        }

        Archived = true;
        ArchivedAt = now;
        UpdatedAt = now;
    }
}
