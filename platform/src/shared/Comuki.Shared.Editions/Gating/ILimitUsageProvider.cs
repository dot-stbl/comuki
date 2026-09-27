using Comuki.Shared.Editions.Catalog.Keys;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// One port per gated limit, implemented by the module that owns the
/// count: a limit like <c>Limits.Projects</c> is owned by the Projects
/// module, so that module ships a concrete
/// <see cref="ILimitUsageProvider"/> exposing the current project count
/// and registers it in DI. <see cref="EditionGate"/> looks up the
/// implementation by <see cref="LimitKey"/> at evaluation time.
/// </summary>
/// <remarks>
/// A limit declared in the catalog but with no registered provider is a
/// wiring gap, not a free pass: <see cref="EditionGate"/> fails closed
/// (denies with a 403) so a missing registration is loudly visible in
/// every environment rather than silently unenforced.
/// </remarks>
public interface ILimitUsageProvider
{
    /// <summary>The limit key this provider reports usage for.</summary>
    public LimitKey LimitKey { get; }

    /// <summary>Current usage right now (a non-negative integer count).</summary>
    /// <param name="cancellationToken">Cancellation token tied to the gating request.</param>
    public Task<int> CurrentAsync(CancellationToken cancellationToken);
}
