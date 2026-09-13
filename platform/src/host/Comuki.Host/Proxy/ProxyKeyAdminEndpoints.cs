using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;

namespace Comuki.Host.Proxy;

/// <summary>
/// Admin surface over the proxy's virtual keys (permission
/// <c>identity:write</c> — credential administration, platform-role only):
/// <list type="bullet">
///   <item><c>GET /api/v1/proxy/keys</c> — catalogue with fingerprint ids and display prefixes; raw tokens never cross the wire.</item>
///   <item><c>POST /api/v1/proxy/keys/{keyId}/revoke</c> — <see cref="IVirtualKeyStore.RemoveAsync"/>: removes the key from the active set with the 60s deletion grace window (Q31). The store is config-seeded — a restart resurrects a revoked key until its <c>Proxy:VirtualKeys</c> row is removed; the response documents that honestly.</item>
///   <item><c>PATCH /api/v1/proxy/keys/{keyId}</c> — <c>501</c>: the config-seeded store is immutable at runtime, so a model-mapping switch cannot be honored.</item>
/// </list>
/// </summary>
public static class ProxyKeyAdminEndpoints
{
    /// <summary>Maps the proxy key admin endpoints.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapProxyKeyAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.ProxyKeys, ListKeysAsync).WithTags("Proxy");
        app.MapPost(ApiRoutes.ProxyKeyRevoke, RevokeKeyAsync).WithTags("Proxy");
        app.MapPatch(ApiRoutes.ProxyKey, PatchKeyAsync).WithTags("Proxy");
        return app;
    }

    [RequiresPermission("identity:write")]
    private static async Task<IResult> ListKeysAsync(
        IVirtualKeyStore keyStore,
        CancellationToken cancellationToken)
    {
        var keys = await keyStore.ListAsync(cancellationToken);
        return Results.Ok(new ProxyKeysResponse(
            [.. keys.Select(static key => new ProxyKeyView(
                VirtualKeyFingerprint.Of(key.Token),
                VirtualKeyFingerprint.PrefixOf(key.Token),
                key.ProjectId.Value,
                key.Upstream.Provider,
                key.Upstream.BaseUrl,
                key.Upstream.DefaultModel,
                key.AllowedModels ?? [],
                key.BudgetUsd,
                key.ExpiresAt))]));
    }

    [RequiresPermission("identity:write")]
    private static async Task<IResult> RevokeKeyAsync(
        string keyId,
        IVirtualKeyStore keyStore,
        CancellationToken cancellationToken)
    {
        var keys = await keyStore.ListAsync(cancellationToken);
        var match = keys.SingleOrDefault(key => VirtualKeyFingerprint.Of(key.Token) == keyId);
        if (match is null)
        {
            return ProxyKeyAdminResults.KeyNotFound(keyId);
        }

        await keyStore.RemoveAsync(match.Token, cancellationToken);
        return Results.NoContent();
    }

    [RequiresPermission("identity:write")]
    private static IResult PatchKeyAsync(string keyId)
    {
        return ProxyKeyAdminResults.KeyMutationUnsupported(keyId);
    }
}

/// <summary>Wire row of one virtual key — fingerprinted; no token material.</summary>
/// <param name="Id">SHA-256 fingerprint of the token — the id revoke addresses.</param>
/// <param name="Prefix">Display prefix of the token.</param>
/// <param name="ProjectId">Project spend attribution.</param>
/// <param name="Provider">Upstream cluster id: <c>openai</c> / <c>anthropic</c> / <c>custom</c>.</param>
/// <param name="BaseUrl">Upstream root URL.</param>
/// <param name="DefaultModel">Default model when the caller's body omits one; null = none.</param>
/// <param name="AllowedModels">Model allow-list; empty = every model permitted.</param>
/// <param name="BudgetUsd">Optional monthly USD cap; null = unlimited.</param>
/// <param name="ExpiresAt">Optional UTC expiry; null = never.</param>
public sealed record ProxyKeyView(
    string Id,
    string Prefix,
    Guid ProjectId,
    string Provider,
    string BaseUrl,
    string? DefaultModel,
    IReadOnlyList<string> AllowedModels,
    decimal? BudgetUsd,
    DateTimeOffset? ExpiresAt);

/// <summary>Keys catalogue envelope.</summary>
/// <param name="Items">Active keys.</param>
public sealed record ProxyKeysResponse(IReadOnlyList<ProxyKeyView> Items);

/// <summary>Problem results of the proxy key admin surface (same shape as the runs surface).</summary>
internal static class ProxyKeyAdminResults
{
    /// <summary>404 — no active key carries the fingerprint.</summary>
    public static IResult KeyNotFound(string keyId)
    {
        return TypedResults.Problem(
            title: "Virtual key not found",
            detail: $"no active virtual key carries fingerprint '{keyId}'",
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "proxy.key_not_found",
                ["keyId"] = keyId,
            });
    }

    /// <summary>
    /// 501 — the store is seeded from <c>Proxy:VirtualKeys</c> at startup
    /// and is immutable at runtime; a model-mapping switch would need a
    /// mutable key store first.
    /// </summary>
    public static IResult KeyMutationUnsupported(string keyId)
    {
        return TypedResults.Problem(
            title: "Virtual key mutation not implemented",
            detail: "virtual keys are seeded from Proxy:VirtualKeys configuration and immutable at runtime; change the configuration and restart",
            statusCode: StatusCodes.Status501NotImplemented,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "proxy.key_mutation_unsupported",
                ["keyId"] = keyId,
            });
    }
}
