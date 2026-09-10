namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Parsed secret reference — the result of splitting an operator string
/// into a routing prefix and a backend-specific path/key. The shape is
/// provider-agnostic: <see cref="Scheme"/> selects the
/// <see cref="ISecretProvider"/>, <see cref="Path"/> is the env-var name
/// or file path, and <see cref="Key"/> is reserved for KV v2 / Consul
/// (slice 2/3) where the path alone is not enough. Env and file
/// providers always pass <see cref="Key"/> = <c>null</c>.
/// </summary>
/// <param name="Scheme">Lowercase scheme prefix (<c>env</c> / <c>file</c> / <c>vault</c> / <c>consul</c>).</param>
/// <param name="Path">Env-var name, file path, or KV v2 path.</param>
/// <param name="Key">KV v2 field key inside the path; null for env/file.</param>
public sealed record SecretRef(string Scheme, string Path, string? Key);
