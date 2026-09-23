using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Workers.VirtualKeys;

/// <summary>
/// A runtime-minted virtual key handed out with one claim: the raw token
/// (appears exactly once, in the claim response) and the worker-facing
/// proxy base URL the Translator stamps as <c>ANTHROPIC_BASE_URL</c>.
/// </summary>
/// <param name="Token">Opaque bearer the worker's pi presents to the proxy.</param>
/// <param name="ProxyBaseUrl">Worker-facing proxy base URL (no trailing slash).</param>
public sealed record MintedVirtualKey(string Token, string ProxyBaseUrl);

/// <summary>
/// Mints and revokes the per-execution proxy capability (issue #122):
/// on a successful claim, when the proxy is enabled and
/// <see cref="ProxyOptions.WorkerBaseUrl"/> is configured, issues a
/// 256-bit opaque token (same generation style as the worker token —
/// 32 random bytes, base64url) into <see cref="IVirtualKeyStore"/> with
/// the claim's lease deadline as the expiry, and remembers it by work
/// item so the terminal paths can revoke. The raw token never leaves
/// this service except once, in the claim response — it is not journaled
/// or logged.
/// </summary>
/// <remarks>
/// Lease-lost needs no explicit revoke: the minted key's expiry equals
/// the lease deadline handed out at claim, so the key dies by expiry
/// exactly when the worker loses the item. Complete and fail call
/// <see cref="RevokeAsync"/> anyway so the key dies at the terminal
/// transition rather than at the (possibly later) deadline. Heartbeats
/// extend the lease in the queue but not the minted key's expiry — a
/// run that outlives its original lease keeps working only until the
/// deadline, by design of the mint contract.
/// Requires <c>AddProxyApplication</c> in the same composition (it
/// provides <see cref="IVirtualKeyStore"/> and the bound
/// <see cref="ProxyOptions"/>).
/// </remarks>
/// <param name="store">The proxy's key catalogue — mints land beside the config-seeded keys.</param>
/// <param name="options">Bound <c>Proxy:*</c> configuration.</param>
/// <param name="logger">Structured logger; logs mint/revoke without the raw token.</param>
public sealed class MintedVirtualKeyService(
    IVirtualKeyStore store,
    IOptions<ProxyOptions> options,
    ILogger<MintedVirtualKeyService> logger)
{
    private const int TokenBytes = 32;

    /// <summary>Minted token by work item — the terminal-path revocation index.</summary>
    private readonly ConcurrentDictionary<Guid, string> tokensByWorkItemId = new();

    /// <summary>
    /// Mints a virtual key for the claimed item, or returns <c>null</c> when
    /// minting is off (proxy disabled or no worker-facing base URL) — the
    /// claim then omits both proxy fields and the worker behaves exactly as
    /// before this capability existed.
    /// </summary>
    /// <param name="projectId">Project of the claimed item's parent run — the spend attribution.</param>
    /// <param name="workItemId">The claimed work item — binds the key to this execution.</param>
    /// <param name="leaseUntil">Lease deadline being returned to the worker; the key expires with it, never later.</param>
    /// <param name="cancellationToken"></param>
    public async Task<MintedVirtualKey?> MintAsync(
        Guid projectId,
        Guid workItemId,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default)
    {
        var proxy = options.Value;
        if (!proxy.Enabled || proxy.WorkerBaseUrl is not { } workerBaseUrl)
        {
            return null;
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        await store.MintAsync(token, new ProjectId(projectId), workItemId, leaseUntil, cancellationToken: cancellationToken);
        tokensByWorkItemId[workItemId] = token;
        logger.LogInformation(
            "Minted virtual key for work item {WorkItemId}, expiring at the lease deadline {LeaseUntil:o}",
            workItemId,
            leaseUntil);
        return new MintedVirtualKey(token, workerBaseUrl.ToString().TrimEnd('/'));
    }

    /// <summary>
    /// Revokes the minted key of a terminal work item (complete / fail).
    /// No-op for unknown ids — an item claimed before this capability (or
    /// by another replica) has nothing to revoke locally.
    /// </summary>
    /// <param name="workItemId">The terminal work item.</param>
    /// <param name="cancellationToken"></param>
    public async Task RevokeAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        if (!tokensByWorkItemId.TryRemove(workItemId, out var token))
        {
            return;
        }

        await store.RemoveAsync(token, cancellationToken);
        logger.LogInformation("Revoked the minted virtual key of work item {WorkItemId}", workItemId);
    }
}
