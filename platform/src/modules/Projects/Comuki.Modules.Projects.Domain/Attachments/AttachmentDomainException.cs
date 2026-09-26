using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Projects.Domain.Attachments;

/// <summary>
/// Semantic invariant violation inside the ProjectRepositoryAttachment
/// aggregate — currently: the access level is
/// <see cref="AttachmentAccess.Unspecified"/> on a freshly created or
/// patched attachment (<c>Unspecified</c> is a placeholder, not a working
/// access level). Maps to HTTP 422 via the shared
/// <see cref="DomainException"/> handler — semantic error, not an
/// upstream failure (502/504).
/// </summary>
/// <param name="code">Stable dot.case identifier.</param>
/// <param name="message">Safe human message — no PII, no secrets.</param>
public sealed class AttachmentDomainException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>Stable code for an attachment's access level being <see cref="AttachmentAccess.Unspecified"/>.</summary>
    public const string AccessInvalid = "projects.attachment_access_invalid";
}
