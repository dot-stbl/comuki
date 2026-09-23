using Comuki.Host.Translator.Api.Models.Responses;

namespace Comuki.Host.Translator.Execution;

/// <summary>
/// Builds the per-execution environment stamp for the pi process from a
/// claim (issue #122): the proxy base URL and the minted virtual key the
/// orchestrator handed out at claim. The dict is consumed by
/// <see cref="Runtime.IPiRunner.RunAsync"/> and applied to that one
/// process — container-level config is never written.
/// </summary>
public static class PiEnvironment
{
    /// <summary>Env var pi reads the model-gateway base URL from.</summary>
    public const string AnthropicBaseUrlVariable = "ANTHROPIC_BASE_URL";

    /// <summary>Env var pi reads the model bearer token from.</summary>
    public const string AnthropicAuthTokenVariable = "ANTHROPIC_AUTH_TOKEN";

    /// <summary>
    /// Maps a claim onto the pi environment stamp: both proxy fields, or
    /// <c>null</c> when the claim carries none (proxy off / unset base
    /// URL) — pi then runs with the inherited environment untouched.
    /// </summary>
    /// <param name="claimed">The claim this cycle executes.</param>
    public static IReadOnlyDictionary<string, string>? FromClaim(ClaimedWorkItemResponse claimed)
    {
        return string.IsNullOrWhiteSpace(claimed.ProxyBaseUrl) || string.IsNullOrWhiteSpace(claimed.VirtualKey)
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AnthropicBaseUrlVariable] = claimed.ProxyBaseUrl,
                [AnthropicAuthTokenVariable] = claimed.VirtualKey,
            };
    }
}
