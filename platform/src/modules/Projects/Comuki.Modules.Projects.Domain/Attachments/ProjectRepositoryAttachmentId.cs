namespace Comuki.Modules.Projects.Domain.Attachments;

/// <summary>
/// Strong-typed identifier of a <see cref="ProjectRepositoryAttachment"/> —
/// the per-Project view of one Repository attached through a role + access
/// pair. UUIDv7 so the "list by project" query benefits from the monotonic
/// order.
/// </summary>
/// <param name="Value"></param>
public readonly record struct ProjectRepositoryAttachmentId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// <returns>A fresh id.</returns>
    public static ProjectRepositoryAttachmentId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
