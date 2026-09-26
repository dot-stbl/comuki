namespace Comuki.Modules.Projects.Domain.Attachments;

/// <summary>
/// The declared access level of a <see cref="ProjectRepositoryAttachment"/>.
/// Closed set: <see cref="External"/>, <see cref="Read"/>, <see cref="Write"/>;
/// <see cref="Unspecified"/> is the <c>default</c> placeholder — never a working
/// attachment (the entity factory refuses it loudly). The lattice meet / effective
/// access resolution lives in the Repositories module's concern (an attachment's
/// effective access is <c>min(attachment.access, credential.DefaultAccess)</c>),
/// not here — the Projects side only carries what was declared.
/// <para>
/// Wire form is PascalCase so EF stores the same identifier the JSON contract
/// surfaces; <see cref="FromWire"/> parses it back; unknown values throw
/// <see cref="ArgumentOutOfRangeException"/> so a corrupt row is loud.
/// </para>
/// </summary>
public readonly record struct AttachmentAccess
{
    private readonly string? value;

    private AttachmentAccess(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — not a real access level; the entity factory rejects it.</summary>
    public static AttachmentAccess Unspecified { get; }

    /// <summary>No credential is resolved; an external change request is the only path forward.</summary>
    public static AttachmentAccess External { get; } = new("External");

    /// <summary>Read-only access; writes are refused even when the credential supports them.</summary>
    public static AttachmentAccess Read { get; } = new("Read");

    /// <summary>Full read-write access; gated by the credential's default access in turn.</summary>
    public static AttachmentAccess Write { get; } = new("Write");

    /// <summary>Wire-form string — the PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working access levels — excludes the placeholder <see cref="Unspecified"/>. One entry per PascalCase wire form.</summary>
    public static IReadOnlyList<AttachmentAccess> All { get; } =
        [External, Read, Write];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire"></param>
    public static AttachmentAccess FromWire(string wire)
    {
        return wire switch
        {
            nameof(External) => External,
            nameof(Read) => Read,
            nameof(Write) => Write,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(AttachmentAccess)} value."),
        };
    }
}
