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

    /// <summary>Upper bound of the stored icon override; mirrored by validation.</summary>
    public const int MaxIconLength = 200;

    /// <summary>Upper bound of distinct tags per project (design D4: counted after normalisation).</summary>
    public const int MaxTags = 20;

    /// <summary>Shape of one normalized tag: lower-case slug, 1–39 chars.</summary>
    public const string TagPattern = "^[a-z0-9][a-z0-9-]{0,38}$";

    /// <summary>Shape of a colour accepted on input: #rrggbb in either letter case.</summary>
    public const string ColorPattern = "^#[0-9A-Fa-f]{6}$";

    /// <summary>Upper bound of <see cref="EnvClass"/> on the wire / in the database (mirrored by validation).</summary>
    public const int MaxEnvClassLength = 128;

    /// <summary>
    /// Shape of an environment-class id accepted on input: lower-case
    /// alphanumeric + dash, 1+ chars. Matches the catalog id format
    /// (see <c>Comuki.Engine.Compute.Environments.EnvironmentBundle</c>).
    /// </summary>
    public const string EnvClassPattern = "^[a-z0-9][a-z0-9-]*$";

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

    /// <summary>
    /// Git URL of the project's source repository (the work-item source code);
    /// optional. Mirrors <see cref="ProfilesGitUrl"/>'s shape and PATCH semantics
    /// (null leaves the stored value untouched on update).
    /// </summary>
    public string? SourceGitUrl { get; private set; }

    /// <summary>Pinned git ref of the source repository (branch, tag or digest); optional.</summary>
    public string? SourceGitRef { get; private set; }

    /// <summary>Operator-chosen identity mark (an emoji or an image URL); an opaque display string stored verbatim.</summary>
    public string? Icon { get; private set; }

    /// <summary>Accent colour stored lower-case (<c>#rrggbb</c>); optional.</summary>
    public string? Color { get; private set; }

    /// <summary>Identity tags — normalized (trimmed, lower-cased, de-duplicated); empty when none.</summary>
    public string[] Tags { get; private set; } = [];

    /// <summary>
    /// Environment-class id the scalar source repository binds to
    /// (catalog id, e.g. <c>"net10-sdk-bun"</c>); empty when unset.
    /// Implements the project stand-in described by the
    /// <c>add-worker-environments</c> change (task 2.2): an empty
    /// value means the project's implement work items are not
    /// claimable until an operator confirms a class.
    /// <para>
    /// Hand-off to <c>add-multi-repo-projects</c>: when that change
    /// migrates <see cref="ProfilesGitUrl"/>'s sibling scalar
    /// <c>SourceGitUrl</c> to a primary <c>ProjectRepositoryAttachment</c>,
    /// <see cref="EnvClass"/> moves onto that <c>Repository</c> row and
    /// the <c>Project</c> field ceases to be source of truth (env-class
    /// lives on the repo, the same as the gate already does for
    /// repository-backed work items). Until that migration lands,
    /// <see cref="EnvClass"/> is the binding for the project's single
    /// scalar source repository.
    /// </para>
    /// </summary>
    public string? EnvClass { get; private set; }

    /// <summary>Soft-archive flag; archived projects keep their runs and settings.</summary>
    public bool Archived { get; private set; }

    /// <summary>When the project was archived; null while active.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>When the project was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a project; the slug is normalized here, uniqueness is backed by the index.</summary>
    public static Project Create(
        string name,
        string slug,
        string? description,
        string? profilesGitUrl,
        string? profilesGitRef,
        DateTimeOffset now,
        string? icon = null,
        string? color = null,
        IReadOnlyList<string>? tags = null,
        string? envClass = null,
        string? sourceGitUrl = null,
        string? sourceGitRef = null)
    {
        return new Project
        {
            Id = ProjectId.New(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description,
            ProfilesGitUrl = profilesGitUrl,
            ProfilesGitRef = profilesGitRef,
            Icon = icon,
            Color = color is null ? null : NormalizeColor(color),
            Tags = NormalizeTags(tags ?? []),
            EnvClass = NormalizeEnvClass(envClass),
            SourceGitUrl = sourceGitUrl,
            SourceGitRef = sourceGitRef,
            Archived = false,
            ArchivedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Normalizes a colour the way it is stored: trimmed and lower-cased
    /// invariantly — the slug school, same as
    /// <see cref="DomainTypes.DomainTypeAdmission.NormalizeKey"/>.
    /// </summary>
    public static string NormalizeColor(string color)
    {
        return color.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes an environment-class id: <c>null</c> passes through
    /// (the scalar Project stays without a class); non-null values are
    /// trimmed. Catalog ids are already lower-case alphanumeric + dash
    /// by convention (see <see cref="EnvClassPattern"/>), so no
    /// case-fold is applied here — the validator enforces the shape.
    /// </summary>
    /// <param name="envClass">Raw input from create / update; null when unset.</param>
    /// <returns>Normalized class id, or null when input is null.</returns>
    public static string? NormalizeEnvClass(string? envClass)
    {
        return envClass?.Trim();
    }

    /// <summary>
    /// Normalizes a tag list: trim + lower-case each entry, drop blanks,
    /// de-duplicate, keep the caller's order. Shape and count rules live in
    /// the application validators — this only reshapes. Materialized as an
    /// array so Npgsql maps it straight onto a <c>text[]</c> column.
    /// </summary>
    public static string[] NormalizeTags(IReadOnlyList<string> tags)
    {
        return
        [
            .. tags
                .Select(static tag => tag.Trim().ToLowerInvariant())
                .Where(static tag => tag.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Partial update: a null field leaves the stored value untouched (PATCH
    /// semantics). The slug is deliberately not editable — it is the stable
    /// external key other modules reference. The one list-vs-scalar
    /// asymmetry: an absent tags list keeps the stored tags, an empty list
    /// clears them (design D5). The <see cref="EnvClass"/> string follows
    /// the same nullable-PATCH shape as <see cref="Description"/>: null
    /// leaves the stored value, an empty string clears it (the operator's
    /// way to take a project back to "no class" without dropping the row).
    /// <see cref="UpdatedAt"/> always moves, even when no field changed.
    /// </summary>
    public void Update(
        string? name,
        string? description,
        string? profilesGitUrl,
        string? profilesGitRef,
        DateTimeOffset now,
        string? icon = null,
        string? color = null,
        IReadOnlyList<string>? tags = null,
        string? envClass = null,
        string? sourceGitUrl = null,
        string? sourceGitRef = null)
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

        if (icon is { } nextIcon)
        {
            Icon = nextIcon;
        }

        if (color is { } nextColor)
        {
            Color = NormalizeColor(nextColor);
        }

        if (tags is { } nextTags)
        {
            Tags = NormalizeTags(nextTags);
        }

        if (envClass is { } nextEnvClass)
        {
            // Empty string clears (matches tags-asymmetry convention); the
            // validator rejects whitespace-only input before we get here.
            EnvClass = nextEnvClass.Length == 0 ? null : NormalizeEnvClass(nextEnvClass);
        }

        if (sourceGitUrl is { } nextSourceUrl)
        {
            SourceGitUrl = nextSourceUrl;
        }

        if (sourceGitRef is { } nextSourceRef)
        {
            SourceGitRef = nextSourceRef;
        }

        UpdatedAt = now;
    }

    /// <summary>Soft-archives the project; archiving twice is a no-op.</summary>
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
