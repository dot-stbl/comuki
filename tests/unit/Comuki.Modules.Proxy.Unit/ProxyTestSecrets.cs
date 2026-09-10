using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>
/// Test double for <see cref="ISecretResolver"/> — keyed by reference
/// string, records every ref it received so tests can assert resolution
/// activity without a real env var / file / KV. File-scoped: the two
/// ConfigurationVirtualKeyStore test files share the same fake.
/// </summary>
file static class ProxyTestSecretsFactory
{
}

internal sealed class ConfigurableSecretResolver : ISecretResolver
{
    public Dictionary<string, string> Map { get; } = [];

    public List<string> Received { get; } = [];

    public Task<string?> ResolveAsync(string? reference, CancellationToken cancellationToken = default)
    {
        if (reference is { Length: > 0 })
        {
            Received.Add(reference);
        }

        return reference is { Length: > 0 } && Map.TryGetValue(reference, out var value)
            ? Task.FromResult<string?>(value)
            : Task.FromResult<string?>(null);
    }
}
