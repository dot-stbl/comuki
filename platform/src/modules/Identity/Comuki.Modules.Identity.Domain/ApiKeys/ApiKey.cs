using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Domain.ApiKeys;

/// <summary>
/// An issued API key. The table stores the public <see cref="Prefix"/>
/// (indexed lookup) and <see cref="KeyHmac"/> — HMAC-SHA256(token, pepper)
/// — never the secret itself. The plaintext exists once, at issue time.
/// </summary>
public sealed class ApiKey
{
    internal ApiKey()
    {
    }

    /// <summary>Strong-typed api key id (UUIDv7).</summary>
    public ApiKeyId Id { get; private set; }

    /// <summary>The owner account; disabling it closes every key.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Human-readable label shown in the UI.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Public 8-char lookup prefix; unique across all keys ever issued.</summary>
    public string Prefix { get; private set; } = string.Empty;

    /// <summary>Lowercase hex HMAC-SHA256 of the full token with the server pepper.</summary>
    public string KeyHmac { get; private set; } = string.Empty;

    /// <summary>When the key was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last successful authentication (throttled write).</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>When the key was revoked; null while active.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Whether the key still authenticates.</summary>
    public bool IsActive => RevokedAt is null;

    /// <summary>Creates a key row from an issue operation.</summary>
    /// <param name="userId"></param>
    /// <param name="name"></param>
    /// <param name="prefix"></param>
    /// <param name="keyHmac"></param>
    /// <param name="now"></param>
    public static ApiKey Create(UserId userId, string name, string prefix, string keyHmac, DateTimeOffset now)
    {
        return new ApiKey
        {
            Id = ApiKeyId.New(),
            UserId = userId,
            Name = name.Trim(),
            Prefix = prefix,
            KeyHmac = keyHmac,
            CreatedAt = now,
        };
    }

    /// <summary>Records a successful use.</summary>
    /// <param name="now"></param>
    public void MarkUsed(DateTimeOffset now)
    {
        LastUsedAt = now;
    }

    /// <summary>Revokes the key; idempotent.</summary>
    /// <param name="now"></param>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
