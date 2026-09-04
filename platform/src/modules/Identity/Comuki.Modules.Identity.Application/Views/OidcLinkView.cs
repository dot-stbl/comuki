using Comuki.Modules.Identity.Domain.Users;

namespace Comuki.Modules.Identity.Application.Views;

/// <summary>
/// Read-model of an OIDC identity link — the local user id, the provider
/// key, and the external subject claim. Provider / subject are returned
/// as the operator entered them (the stored form lower-cases the provider
/// key; the subject is preserved verbatim).
/// </summary>
/// <param name="Id"></param>
/// <param name="UserId"></param>
/// <param name="Provider"></param>
/// <param name="Subject"></param>
/// <param name="CreatedAt"></param>
public sealed record OidcLinkView(
    Guid Id,
    Guid UserId,
    string Provider,
    string Subject,
    DateTimeOffset CreatedAt)
{
    /// <summary>Maps the domain entity.</summary>
    /// <param name="link"></param>
    /// <returns></returns>
    public static OidcLinkView Of(OidcLink link)
    {
        return new OidcLinkView(
            link.Id.Value,
            link.UserId.Value,
            link.Provider,
            link.Subject,
            link.CreatedAt);
    }
}
