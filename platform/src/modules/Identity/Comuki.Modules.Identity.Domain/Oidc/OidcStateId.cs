namespace Comuki.Modules.Identity.Domain.Oidc;

/// <summary>
/// Strong-typed identifier of an OIDC state row. UUIDv7 — the canonical
/// external state token the host issues to the browser is the row id
/// itself (URL-safe, opaque, single-use). Database lookup is by
/// <see cref="Value"/>, never by anything user-visible.
/// </summary>
/// <param name="Value">The underlying UUIDv7.</param>
public readonly record struct OidcStateId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static OidcStateId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString("D");
    }
}
