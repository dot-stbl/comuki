namespace Comuki.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Authentication scheme names of the Identity module. The cookie is the
/// default scheme (browser sessions); the API-key scheme is selected by
/// the bearer-token forward selector; per-provider OIDC schemes are
/// suffixed with the configured provider name.
/// </summary>
public static class AuthSchemes
{
    /// <summary>Interactive browser sessions.</summary>
    public const string Cookie = "Comuki.Cookie";

    /// <summary><c>Authorization: Bearer ck_…</c> tokens.</summary>
    public const string ApiKey = "Comuki.ApiKey";

    /// <summary>Scheme name of one configured OIDC provider.</summary>
    /// <param name="provider"></param>
    /// <returns></returns>
    public static string Oidc(string provider)
    {
        return $"Comuki.Oidc.{provider}";
    }
}
