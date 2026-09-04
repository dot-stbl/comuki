using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;

namespace Comuki.Modules.Proxy.Application.Resolving;

/// <summary>
/// Applies the lifetime policy of a virtual key: token presence,
/// expiry check, allowed-model check. The auth handler hands the raw
/// bearer to <see cref="ResolveAsync"/> and gets back a verdict the
/// challenge pipeline turns into 401 / 403 / 200.
/// </summary>
/// <param name="store">Underlying key catalogue (configuration by default).</param>
/// <param name="clock">Wall-clock for expiry comparison.</param>
public sealed class VirtualKeyResolver(IVirtualKeyStore store, TimeProvider clock)
{
    /// <summary>Lookup result.</summary>
    /// <param name="Outcome">Resolution verdict.</param>
    /// <param name="Key">The matching key when <see cref="Outcome"/> is <see cref="ResolveOutcome.Resolved"/>.</param>
    public sealed record Resolution(ResolveOutcome Outcome, VirtualKey? Key = null);

    /// <summary>Resolution verdict.</summary>
    public enum ResolveOutcome
    {
        /// <summary>No token / token not found.</summary>
        Missing = 0,

        /// <summary>Token matched but is past <see cref="VirtualKey.ExpiresAt"/>.</summary>
        Expired = 1,

        /// <summary>Token matched but the requested model is outside <see cref="VirtualKey.AllowedModels"/>.</summary>
        ModelNotAllowed = 2,

        /// <summary>Token matched, not expired, model permitted (when applicable).</summary>
        Resolved = 3,
    }

    /// <summary>Resolves the raw bearer against the store and applies the lifetime checks.</summary>
    /// <param name="token">Raw bearer token.</param>
    /// <param name="requestedModel">Model the caller asked for (may be null when the body omitted it).</param>
    /// <param name="cancellationToken"></param>
    public async Task<Resolution> ResolveAsync(string? token, string? requestedModel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new Resolution(ResolveOutcome.Missing);
        }

        var key = await store.FindAsync(token, cancellationToken);
        if (key is null)
        {
            return new Resolution(ResolveOutcome.Missing);
        }

        var modelNotAllowed = key.AllowedModels is { Count: > 0 } allowed
                              && !string.IsNullOrWhiteSpace(requestedModel)
                              && !allowed.Contains(requestedModel, StringComparer.Ordinal);
        var expired = key.ExpiresAt is { } expiresAt && clock.GetUtcNow() >= expiresAt;

        return expired
            ? new Resolution(ResolveOutcome.Expired, key)
            : modelNotAllowed
                ? new Resolution(ResolveOutcome.ModelNotAllowed, key)
                : new Resolution(ResolveOutcome.Resolved, key);
    }
}
