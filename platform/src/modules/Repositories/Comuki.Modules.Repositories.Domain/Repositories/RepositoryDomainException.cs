using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Repositories.Domain.Repositories;

/// <summary>
/// Semantic invariant violation inside the Repositories module — currently:
/// the credential reference is created with <see cref="RepositoryAccess.Unspecified"/>
/// or <see cref="RepositoryAccess.External"/> as its <c>DefaultAccess</c>
/// (only <see cref="RepositoryAccess.Read"/> and <see cref="RepositoryAccess.Write"/>
/// are legal defaults; <c>Unspecified</c> is a placeholder and <c>External</c>
/// by definition never resolves a credential). Maps to HTTP 422 via the
/// shared <see cref="DomainException"/> handler — semantic error, not an
/// upstream failure (502/504).
/// </summary>
/// <param name="code">Stable dot.case identifier.</param>
/// <param name="message">Safe human message — no PII, no secrets.</param>
public sealed class RepositoryDomainException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>Stable code for a credential <c>DefaultAccess</c> outside the <c>Read</c>|<c>Write</c> set.</summary>
    public const string DefaultAccessInvalid = "repository.default_access_invalid";
}
