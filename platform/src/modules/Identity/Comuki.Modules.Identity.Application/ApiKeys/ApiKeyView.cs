using Comuki.Modules.Identity.Domain.ApiKeys;

namespace Comuki.Modules.Identity.Application.ApiKeys;

/// <summary>
/// Read-model of an API key row. The plaintext token is NOT carried —
/// it lives in <see cref="Issue.IssuedApiKeyCredential"/>
/// only at issue time.
/// </summary>
/// <param name="Id"></param>
/// <param name="UserId"></param>
/// <param name="Name"></param>
/// <param name="Prefix"></param>
/// <param name="CreatedAt"></param>
/// <param name="LastUsedAt"></param>
/// <param name="RevokedAt"></param>
/// <param name="IsActive"></param>
public sealed record ApiKeyView(
    Guid Id,
    Guid UserId,
    string Name,
    string Prefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    bool IsActive)
{
    /// <summary>Maps the domain entity.</summary>
    /// <param name="apiKey"></param>
    /// <returns></returns>
    public static ApiKeyView Of(ApiKey apiKey)
    {
        return new ApiKeyView(
            apiKey.Id.Value,
            apiKey.UserId.Value,
            apiKey.Name,
            apiKey.Prefix,
            apiKey.CreatedAt,
            apiKey.LastUsedAt,
            apiKey.RevokedAt,
            apiKey.IsActive);
    }
}
