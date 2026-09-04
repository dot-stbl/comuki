namespace Comuki.Modules.Proxy.Application.Models;

/// <summary>
/// Where one virtual key forwards traffic to. <see cref="Provider"/> selects
/// a stable YARP cluster id (<c>openai</c> / <c>anthropic</c> /
/// <c>custom</c>); <see cref="BaseUrl"/> is the upstream root URL the
/// transformer uses to rewrite the path. <see cref="ApiKeyEnvRef"/> names the
/// env var holding the upstream API key — secrets stay in the environment,
/// never in the config file.
/// </summary>
/// <param name="Provider">Cluster id: <c>openai</c>, <c>anthropic</c>, or <c>custom</c>.</param>
/// <param name="BaseUrl">Upstream root URL (e.g. <c>https://api.openai.com</c>).</param>
/// <param name="ApiKeyEnvRef">Env var name the proxy reads the upstream API key from at startup.</param>
/// <param name="DefaultModel">Default model the upstream sees when the caller's body omits <c>model</c>.</param>
public sealed record UpstreamSpec(
    string Provider,
    string BaseUrl,
    string ApiKeyEnvRef,
    string? DefaultModel = null);
