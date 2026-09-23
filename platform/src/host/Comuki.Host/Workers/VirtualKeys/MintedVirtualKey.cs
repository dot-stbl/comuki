namespace Comuki.Host.Workers.VirtualKeys;

/// <summary>
/// A runtime-minted virtual key handed out with one claim: the raw token
/// (appears exactly once, in the claim response) and the worker-facing
/// proxy base URL the Translator stamps as <c>ANTHROPIC_BASE_URL</c>.
/// </summary>
/// <param name="Token">Opaque bearer the worker's pi presents to the proxy.</param>
/// <param name="ProxyBaseUrl">Worker-facing proxy base URL (no trailing slash).</param>
public sealed record MintedVirtualKey(string Token, string ProxyBaseUrl);
