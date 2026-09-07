using Comuki.Modules.Identity.Application.Ports;

namespace Comuki.Modules.Identity.Application.ApiKeys.List;

/// <summary>
/// Reads a page of API keys (active + revoked) for the identity-admin
/// list (<c>GET /api/v1/keys</c>, issue #45 / F13). The plaintext token is
/// never carried: <see cref="ApiKeyView"/> carries only the public-facing
/// fields (prefix, name, status, timestamps). The store returns a page
/// sorted by creation time (newest first) plus the unfiltered total.
/// </summary>
/// <param name="apiKeys">Persistence port (scoped — DbContext per request).</param>
public sealed class ListApiKeysHandler(IApiKeyStore apiKeys)
{
    /// <summary>Reads the requested page.</summary>
    /// <param name="query"></param>
    /// <param name="cancellationToken"></param>
    public async Task<(IReadOnlyList<ApiKeyView> Items, int Total)> HandleAsync(
        ListApiKeysQuery query,
        CancellationToken cancellationToken = default)
    {
        var skip = (query.Page - 1) * query.PageSize;
        var (rows, total) = await apiKeys.ListAsync(
            query.UserId,
            skip,
            query.PageSize,
            cancellationToken);

        var items = new ApiKeyView[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            items[index] = ApiKeyView.Of(rows[index]);
        }

        return (items, total);
    }
}
