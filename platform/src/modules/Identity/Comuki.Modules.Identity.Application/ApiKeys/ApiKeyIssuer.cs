using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Identity.Application.ApiKeys;

/// <summary>
/// Issues API keys: generates the token, stores only its HMAC, hands the
/// plaintext back exactly once. The issuer never checks the owner — that
/// guard belongs to the handler/command layer which knows the user. An
/// optional tenant scope binds the key to one project; when set, the
/// auth handler requires the matching <c>X-Comuki-Tenant</c> header.
/// </summary>
/// <param name="apiKeyStore"></param>
/// <param name="hasher"></param>
/// <param name="clock"></param>
public sealed class ApiKeyIssuer(
    IApiKeyStore apiKeyStore,
    ApiKeyHasher hasher,
    TimeProvider clock)
{
    /// <summary>Issues a fresh key for the user and persists its HMAC.</summary>
    /// <param name="userId"></param>
    /// <param name="name"></param>
    /// <param name="tenantProjectId">
    /// Optional tenant scope. When set, the key only authenticates
    /// requests that carry the matching <c>X-Comuki-Tenant</c> header.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IssuedApiKeyCredential> IssueAsync(
        UserId userId,
        string name,
        ProjectId? tenantProjectId,
        CancellationToken cancellationToken = default)
    {
        var token = ApiKeyToken.New();
        var apiKey = ApiKey.Create(
            userId,
            name,
            token.Prefix,
            hasher.Hash(token.ToString()),
            clock.GetUtcNow(),
            tenantProjectId);

        await apiKeyStore.SaveAsync(apiKey, cancellationToken);

        return new IssuedApiKeyCredential(
            apiKey.Id,
            apiKey.Name,
            apiKey.Prefix,
            apiKey.TenantProjectId?.Value,
            token.ToString());
    }
}
