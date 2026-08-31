using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Compute.Pool;

/// <summary>One worker as tracked by the supervisor's pool registry.</summary>
/// <param name="Id">Runtime worker id from the provider handle — the stop path uses it.</param>
/// <param name="TokenId">
///     Worker id the start token was issued FOR. The provider mints its own
///     worker id inside <c>StartAsync</c>, so it differs from the id the
///     supervisor pre-issued the token for; the stop path revokes via
///     <c>TokenId</c>. Equals <paramref name="Id" /> for adopted workers with
///     no known token.
/// </param>
/// <param name="ProjectId">Owning project.</param>
/// <param name="ProfileKey">Profile the worker was started for (claim matching).</param>
/// <param name="ProviderRef">Container id / job name at the provider.</param>
/// <param name="LastActiveAt">Last start/claim/heartbeat time — feeds the idle TTL.</param>
/// <param name="IsBusy">True while the worker holds a claimed work item.</param>
public sealed record PoolWorker(
    WorkerId Id,
    WorkerId TokenId,
    ProjectId ProjectId,
    string ProfileKey,
    string ProviderRef,
    DateTimeOffset LastActiveAt,
    bool IsBusy);
