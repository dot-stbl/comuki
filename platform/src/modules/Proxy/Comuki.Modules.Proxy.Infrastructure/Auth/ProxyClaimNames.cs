namespace Comuki.Modules.Proxy.Infrastructure.Auth;

/// <summary>Stable claim names emitted by <see cref="VirtualKeyAuthenticationHandler"/>.</summary>
public static class ProxyClaimNames
{
    /// <summary>Raw virtual-key token; the transformer reads it to swap in the upstream key.</summary>
    public const string VirtualKey = "comuki.proxy.vkey";

    /// <summary>Project the spend is attributed to.</summary>
    public const string ProjectId = "comuki.proxy.project_id";

    /// <summary>Configured upstream provider id (<c>openai</c> / <c>anthropic</c>).</summary>
    public const string Provider = "comuki.proxy.provider";
}
