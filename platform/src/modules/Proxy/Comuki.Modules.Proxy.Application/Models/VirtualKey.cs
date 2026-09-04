using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Proxy.Application.Models;

/// <summary>
/// One configured virtual key the proxy recognises. Mints a project
/// attribution, an upstream target, an optional monthly USD budget and an
/// optional expiry. The token itself is the lookup key (the proxy never
/// logs it; the auth handler only compares equality in-memory).
/// </summary>
/// <param name="Token">Opaque bearer string the caller presents (<c>Authorization: Bearer &lt;token&gt;</c>).</param>
/// <param name="ProjectId">Project spend attribution — every recorded usage event lands against it.</param>
/// <param name="Upstream">Where the request is forwarded to.</param>
/// <param name="BudgetUsd">Optional monthly USD cap; <c>null</c> = unlimited.</param>
/// <param name="ExpiresAt">Optional UTC instant after which the key is rejected; <c>null</c> = never.</param>
/// <param name="AllowedModels">Optional model allow-list; empty / null = every model permitted.</param>
public sealed record VirtualKey(
    string Token,
    ProjectId ProjectId,
    UpstreamSpec Upstream,
    decimal? BudgetUsd = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<string>? AllowedModels = null);
