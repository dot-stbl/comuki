using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Domain.Users;

/// <summary>
/// A user account — local password or OIDC-linked. <see cref="PasswordHash"/>
/// is null for OIDC-only accounts; <see cref="TokensVersion"/> is the
/// security-stamp analogue: bumping it invalidates every cookie issued
/// for the account (API keys are unaffected — they carry no stamp).
/// </summary>
public sealed class User
{
    internal User()
    {
    }

    /// <summary>Strong-typed user id (UUIDv7).</summary>
    public UserId Id { get; private set; }

    /// <summary>Login email, stored lower-cased; unique index in the database.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Human-readable name shown in the UI.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>PasswordHasher-format hash; null for OIDC-only accounts.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>Security-stamp counter; embedded into login cookies.</summary>
    public int TokensVersion { get; private set; }

    /// <summary>Disabled accounts fail login and every API-key check.</summary>
    public bool Disabled { get; private set; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an account; passwordHash may be null for OIDC-only users.</summary>
    /// <param name="email"></param>
    /// <param name="displayName"></param>
    /// <param name="passwordHash"></param>
    /// <param name="now"></param>
    public static User Create(string email, string displayName, string? passwordHash, DateTimeOffset now)
    {
        return new User
        {
            Id = UserId.New(),
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
            TokensVersion = 1,
            Disabled = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Sets or replaces the password hash and invalidates existing cookies.</summary>
    /// <param name="passwordHash"></param>
    /// <param name="now"></param>
    public void SetPassword(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        BumpTokensVersion(now);
    }

    /// <summary>Bumps the security stamp — every outstanding cookie dies at its next validation.</summary>
    /// <param name="now"></param>
    public void BumpTokensVersion(DateTimeOffset now)
    {
        TokensVersion++;
        UpdatedAt = now;
    }

    /// <summary>Disables the account and kills its cookie sessions.</summary>
    /// <param name="now"></param>
    public void Disable(DateTimeOffset now)
    {
        Disabled = true;
        BumpTokensVersion(now);
    }

    /// <summary>Re-enables the account.</summary>
    /// <param name="now"></param>
    public void Enable(DateTimeOffset now)
    {
        Disabled = false;
        UpdatedAt = now;
    }
}
