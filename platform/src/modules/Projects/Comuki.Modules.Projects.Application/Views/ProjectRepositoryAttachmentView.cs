using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Views;

/// <summary>Read model of a project repository attachment — everything the operational UI needs, nothing internal.</summary>
public sealed record ProjectRepositoryAttachmentView
{
    /// <summary>Attachment id.</summary>
    public required ProjectRepositoryAttachmentId Id { get; init; }

    /// <summary>Owning project.</summary>
    public required ProjectId ProjectId { get; init; }

    /// <summary>Attached repository (the same <see cref="Guid"/> bytes the Repositories module uses).</summary>
    public required RepositoryId RepositoryId { get; init; }

    /// <summary>Wire-form role string (trim + lower-case, invariant culture); the open set means it is a string, not an enum.</summary>
    public required string Role { get; init; }

    /// <summary>Declared access level.</summary>
    public required AttachmentAccess Access { get; init; }

    /// <summary>Optional per-attachment credential override ref.</summary>
    public required string? CredentialOverrideRef { get; init; }

    /// <summary>Creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last mutation timestamp.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
