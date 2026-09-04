namespace Comuki.Modules.Identity.Domain.Oidc;

/// <summary>
/// One in-flight OIDC authorization flow. The id is the opaque
/// <c>state</c> the host hands to the IdP — a UUIDv7 the browser cannot
/// guess and a follow-up row cannot reconstruct, so a missing row
/// means "rejected" without ever needing a separate signature.
/// <para>
/// <see cref="CodeVerifier"/> is the PKCE secret paired with the
/// <c>code_challenge</c> sent in the authorize call; it travels with the
/// row, never to the browser. <see cref="RedirectUri"/> is what we
/// expect the IdP to call back to — it is used to verify the
/// <c>redirect_uri</c> at token-exchange time. <see cref="ReturnTo"/> is
/// the in-app path the operator was bounced from; it survives the
/// round-trip verbatim and is the redirect target on success.
/// </para>
/// <para>
/// <see cref="ExpiresAt"/> is UTC; the store purges anything past it on
/// read (single-use, atomic) and a periodic sweep removes dead rows.
/// Five minutes is the default — long enough for a slow IdP + a
/// distracted operator, short enough that a leaked state token expires
/// before it matters.
/// </para>
/// </summary>
public sealed class OidcState
{
    internal OidcState()
    {
    }

    /// <summary>Opaque state token — the URL-safe UUIDv7 the browser carries.</summary>
    public OidcStateId Id { get; private set; }

    /// <summary>Provider name as configured in <c>auth:oidc:providers</c>.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>The PKCE verifier — paired with the challenge sent at authorize time.</summary>
    public string CodeVerifier { get; private set; } = string.Empty;

    /// <summary>The PKCE challenge method used at authorize time; always <c>S256</c>.</summary>
    public string CodeChallengeMethod { get; private set; } = "S256";

    /// <summary>The redirect_uri the IdP will call back to; verified at token exchange.</summary>
    public string RedirectUri { get; private set; } = string.Empty;

    /// <summary>Optional in-app return path the operator was bounced from.</summary>
    public string? ReturnTo { get; private set; }

    /// <summary>UTC instant the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>UTC instant after which the row is dead — single-use, then GC.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Creates an in-flight row. PKCE verifier is the raw secret — never log it.</summary>
    /// <param name="provider"></param>
    /// <param name="codeVerifier"></param>
    /// <param name="codeChallengeMethod"></param>
    /// <param name="redirectUri"></param>
    /// <param name="returnTo"></param>
    /// <param name="now"></param>
    /// <param name="ttl"></param>
    public static OidcState Create(
        string provider,
        string codeVerifier,
        string codeChallengeMethod,
        string redirectUri,
        string? returnTo,
        DateTimeOffset now,
        TimeSpan ttl)
    {
        return new OidcState
        {
            Id = OidcStateId.New(),
            Provider = provider.Trim().ToLowerInvariant(),
            CodeVerifier = codeVerifier,
            CodeChallengeMethod = codeChallengeMethod,
            RedirectUri = redirectUri,
            ReturnTo = returnTo,
            CreatedAt = now,
            ExpiresAt = now.Add(ttl),
        };
    }
}
