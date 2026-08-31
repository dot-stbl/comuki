namespace Comuki.Modules.Identity.Domain.Users;

/// <summary>Strong-typed identifier of an OIDC link row. UUIDv7.</summary>
/// <param name="Value"></param>
public readonly record struct OidcLinkId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static OidcLinkId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
