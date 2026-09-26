namespace Comuki.Modules.Projects.Domain.Attachments;

/// <summary>
/// Open-set value type for the role of a <see cref="ProjectRepositoryAttachment"/>.
/// The well-known roles (<see cref="Primary"/>, <see cref="Service"/>,
/// <see cref="Frontend"/>, <see cref="Library"/>, <see cref="DeployGitOps"/>,
/// <see cref="Docs"/>) are the spec-defined defaults but the spec role list
/// ends with "<c>...</c>" — new roles are legitimate extensions and
/// <see cref="FromWire"/> accepts any non-empty normalized value rather than
/// refusing unknown ones. An empty/whitespace wire form is the only thing
/// that throws, because a role is the row's meaning and "no role" is not
/// a meaningful attachment.
/// <para>
/// Values are stored normalized (trim + lower-case, invariant culture);
/// <see cref="Normalize"/> is public so the application layer and the store
/// look up rows with the very same key the entity persisted.
/// </para>
/// </summary>
public readonly record struct AttachmentRole
{
    private readonly string? value;

    private AttachmentRole(string value)
    {
        this.value = value;
    }

    /// <summary>Maximum length of a stored role value; mirrored by the column length.</summary>
    public const int MaxLength = 64;

    /// <summary>The Project's primary repository — the one whose commits are the project's own output.</summary>
    public static AttachmentRole Primary { get; } = new("primary");

    /// <summary>A backend service the Project consumes from.</summary>
    public static AttachmentRole Service { get; } = new("service");

    /// <summary>A frontend codebase the Project consumes from.</summary>
    public static AttachmentRole Frontend { get; } = new("frontend");

    /// <summary>A reusable library the Project depends on.</summary>
    public static AttachmentRole Library { get; } = new("library");

    /// <summary>A deploy / GitOps repository that ships the Project.</summary>
    public static AttachmentRole DeployGitOps { get; } = new("deploy-gitops");

    /// <summary>The Project's docs repository.</summary>
    public static AttachmentRole Docs { get; } = new("docs");

    /// <summary>Default placeholder — not a working role; the factory refuses attachments built on it.</summary>
    public static AttachmentRole Unspecified { get; }

    /// <summary>Wire-form string — the normalized text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>
    /// Normalizes a role string the way the stored rows are normalized
    /// (trim + lower-case, invariant culture). Public so the application
    /// layer and the store look up rows with the very same key the entity
    /// persisted.
    /// </summary>
    /// <param name="role"></param>
    /// <returns></returns>
    public static string Normalize(string role)
    {
        return role.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Parse a stored wire-form string back into the smart-type. Accepts any
    /// non-empty normalized value (open set — new roles are stored, not
    /// thrown on); empty/whitespace is rejected with
    /// <see cref="ArgumentException"/> because a row without a role has
    /// no meaning.
    /// </summary>
    /// <param name="wire"></param>
    public static AttachmentRole FromWire(string wire)
    {
        var normalized = Normalize(wire);
        return normalized.Length switch
        {
            0 => throw new ArgumentException("role must not be empty or whitespace.", nameof(wire)),
            _ => normalized switch
            {
                nameof(Primary) => Primary,
                nameof(Service) => Service,
                nameof(Frontend) => Frontend,
                nameof(Library) => Library,
                nameof(DeployGitOps) => DeployGitOps,
                nameof(Docs) => Docs,
                _ => new AttachmentRole(normalized),
            },
        };
    }
}
