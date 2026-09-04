using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Proxy.Application.Options;

/// <summary>
/// Configuration for the optional OpenAI / Anthropic-compatible proxy
/// (issue #8 / S9 T9.6). Bound from <c>Proxy:*</c> configuration; the
/// section is optional — the whole module is disabled when
/// <see cref="Enabled"/> is <c>false</c> or the host finds no virtual keys.
/// Virtual keys live in <see cref="VirtualKeys"/>; reload requires a host
/// restart for v1 (Postgres-backed key storage is a follow-up).
/// </summary>
public sealed class ProxyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Proxy";

    /// <summary>Master switch. When <c>false</c> <see cref="ProxyApplicationExtensions.AddProxyApplication"/> registers no routes.</summary>
    public bool Enabled { get; init; }

    /// <summary>Per-token pricing fallback (USD per million tokens). Used when a model id is missing from <see cref="Pricing"/>.</summary>
    public PricingTier DefaultPricing { get; init; } = new();

    /// <summary>Per-model pricing overrides (key = model id, value = per-million-token USD).</summary>
    public IReadOnlyDictionary<string, PricingTier> Pricing { get; init; } =
        new Dictionary<string, PricingTier>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Models the proxy advertises on <c>GET /v1/models</c>.</summary>
    public IReadOnlyList<string> KnownModels { get; init; } = [];

    /// <summary>Configured virtual keys. Order does not matter; tokens are unique.</summary>
    public IReadOnlyList<VirtualKeyConfiguration> VirtualKeys { get; init; } = [];

    /// <summary>One row of <see cref="VirtualKeys"/>.</summary>
    public sealed class VirtualKeyConfiguration
    {
        /// <summary>Opaque token the caller presents (<c>Authorization: Bearer &lt;token&gt;</c>).</summary>
        [Required]
        [MinLength(16)]
        public string Token { get; init; } = string.Empty;

        /// <summary>UUID identifying the project the spend belongs to.</summary>
        [Required]
        public Guid ProjectId { get; init; }

        /// <summary>Provider cluster id (<c>openai</c> / <c>anthropic</c> / <c>custom</c>).</summary>
        [Required]
        public string Provider { get; init; } = string.Empty;

        /// <summary>Upstream base URL (e.g. <c>https://api.openai.com</c>).</summary>
        [Required]
        [Url]
        public string BaseUrl { get; init; } = string.Empty;

        /// <summary>Env var the proxy reads the upstream API key from.</summary>
        [Required]
        public string ApiKeyEnvRef { get; init; } = string.Empty;

        /// <summary>Default model the upstream sees when the caller's body omits <c>model</c>.</summary>
        public string? DefaultModel { get; init; }

        /// <summary>Optional monthly USD cap; <c>null</c> = unlimited.</summary>
        [Range(0.01, 1_000_000)]
        public decimal? BudgetUsd { get; init; }

        /// <summary>Optional UTC instant after which the key is rejected.</summary>
        public DateTimeOffset? ExpiresAt { get; init; }

        /// <summary>Optional model allow-list; <c>null</c> / empty = every model permitted.</summary>
        public IReadOnlyList<string>? AllowedModels { get; init; }
    }

    /// <summary>USD per million tokens for input and output.</summary>
    public sealed record PricingTier(decimal InputUsdPerMillion = 3m, decimal OutputUsdPerMillion = 15m)
    {
        /// <summary>Default OpenAI-style pricing.</summary>
        public PricingTier()
            : this(InputUsdPerMillion: 3m, OutputUsdPerMillion: 15m)
        {
        }
    }
}
