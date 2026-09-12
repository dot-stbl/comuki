using Comuki.Modules.Identity.Domain.ApiKeys;

namespace Comuki.Modules.Identity.Application.ApiKeys;

/// <summary>
/// Read-model of an API key row. The plaintext token is NOT carried —
/// it lives in <see cref="IssuedApiKeyCredential"/>
/// only at issue time.
/// </summary>
/// <param name="Id"></param>
/// <param name="UserId"></param>
/// <param name="Name"></param>
/// <param name="Prefix"></param>
/// <param name="TenantProjectId">
/// Tenant scope the key was issued under. Null when the key has no
/// tenant scope and accepts any <c>X-Comuki-Tenant</c> header value.
/// </param>
/// <param name="CreatedAt"></param>
/// <param name="LastUsedAt"></param>
/// <param name="RevokedAt"></param>
/// <param name="IsActive"></param>
public sealed record ApiKeyView(
    Guid Id,
    Guid UserId,
    string Name,
    string Prefix,
    Guid? TenantProjectId,
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
            apiKey.TenantProjectId?.Value,
            apiKey.CreatedAt,
            apiKey.LastUsedAt,
            apiKey.RevokedAt,
            apiKey.IsActive);
    }
}
