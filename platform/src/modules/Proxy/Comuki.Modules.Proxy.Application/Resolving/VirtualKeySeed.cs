using System.Collections.Concurrent;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Proxy.Application.Resolving;

/// <summary>
/// Owns the lazy-seed logic for <see cref="ConfigurationVirtualKeyStore"/>:
/// runs the async seed build exactly once and applies the snapshot to the
/// store's dictionary. The flag tracking prevents a remove-then-find
/// sequence from re-populating a deleted key from the snapshot
/// (the dictionary is empty after a remove, so a naive <c>byToken.IsEmpty</c>
/// guard would re-apply the seed on every FindAsync). Pure helper —
/// no DI beyond the resolver and options it already needs. The snapshot
/// itself is built by <see cref="VirtualKeySeedBuilder"/> (separate class
/// per <c>code-shape.md</c> §1a — no private methods in production).
/// </summary>
/// <param name="options">Bound <c>Proxy:*</c> configuration.</param>
/// <param name="secrets">Shared-kernel resolver — routes by scheme to the matching provider.</param>
/// <param name="logger">Structured logger; warns when a referenced ref resolves empty.</param>
public sealed class VirtualKeySeed(
    IOptions<ProxyOptions> options,
    ISecretResolver secrets,
    ILogger<VirtualKeySeed> logger)
{
    private readonly Lazy<Task<IDictionary<string, VirtualKey>>> seedTask = new(() =>
        VirtualKeySeedBuilder.BuildAsync(options.Value, secrets, logger, CancellationToken.None));

    private bool applied;
    private readonly Lock appliedGate = new();

    /// <summary>Apply the seed snapshot to <paramref name="target"/> exactly once.</summary>
    /// <param name="target">The store's dictionary — the seed mutates it in-place under a lock.</param>
    /// <param name="cancellationToken"></param>
    public async Task EnsureAppliedAsync(
        ConcurrentDictionary<string, VirtualKey> target,
        CancellationToken cancellationToken)
    {
        if (applied)
        {
            return;
        }

        var snapshot = await seedTask.Value.WaitAsync(cancellationToken);
        lock (appliedGate)
        {
            if (applied)
            {
                return;
            }

            foreach (var (token, key) in snapshot)
            {
                target[token] = key;
            }

            applied = true;
        }
    }
}
