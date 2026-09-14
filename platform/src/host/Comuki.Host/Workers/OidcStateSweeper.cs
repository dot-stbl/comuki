using Comuki.Modules.Identity.Application.Ports;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Workers;

/// <summary>
/// OIDC state sweeper behind the comuki worker registry: prunes the OIDC
/// state table on a fixed interval. The OIDC start handler issues
/// 5-minute-TTL rows; the sweeper is the bounded-leak defence for flows
/// the operator abandoned (closed tab, IdP timeout, browser back).
/// Without this the table grows forever; with it the table stays at
/// <c>~ N_operators * one_in_flight</c>.
/// <para>
/// The store is scoped (one DbContext per unit of work), so each sweep
/// opens a fresh DI scope. Sweep failures are reported to the registry,
/// which logs and retries with backoff — a transient DB error must NOT
/// take the host down.
/// </para>
/// <para>
/// Issue Q30 / v1.1: every cycle probes
/// <c>information_schema.tables</c> for the <c>oidc_states</c> table. A
/// fresh deploy whose migrator has not yet run fails the cycle with a
/// <c>Critical</c> log and the exact remediation message; the registry
/// backs off and re-probes, so an operator who runs the migrator
/// mid-flight sees the next probe flip to healthy without a restart.
/// </para>
/// </summary>
/// <param name="scopeFactory">DI scope factory — the store is scoped.</param>
/// <param name="options">Bound from <c>Host:OidcSweep</c>; the interval drives the schedule.</param>
/// <param name="clock">Injected to keep the cutoff testable.</param>
/// <param name="logger">Structured logger — Information on count, Critical on the missing-table probe.</param>
public sealed class OidcStateSweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<OidcSweepOptions> options,
    TimeProvider clock,
    ILogger<OidcStateSweeper> logger) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "oidc-sweep";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(options.Value.Interval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        // Q30 / v1.1: probe every cycle so a missing-table failure is loud
        // and repeats until the migrator lands; we do not refuse to run.
        if (!await TableExistsAsync(cancellationToken))
        {
            logger.LogCritical(
                "migrator not run, oidc_states table missing; sweeper will retry with backoff. Run the migrator (see .agents/docs/operations/runbook.md) and the next sweep will succeed without a host restart.");

            return WorkerResult.Fail("oidc_states table missing");
        }

        var deleted = await SweepOnceAsync(cancellationToken);

        return WorkerResult.Ok($"deleted {deleted}", deleted);
    }

    /// <summary>Runs one sweep now — also the test entry point.</summary>
    /// <param name="cancellationToken"></param>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOidcStateStore>();

        var cutoff = clock.GetUtcNow() - options.Value.StateTtl;
        var deleted = await store.DeleteExpiredAsync(cutoff, cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Swept {SweptCount} expired OIDC state row(s)", deleted);
        }

        return deleted;
    }

    /// <summary>
    /// Q30 / v1.1: ask the store whether its table is actually present in
    /// the database the migrator populated. A probe error counts as a
    /// failed cycle (the registry retries); it never throws.
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task<bool> TableExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IOidcStateStore>();

            return await store.TableExistsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or System.Data.Common.DbException)
        {
            logger.LogWarning(exception, "could not probe oidc_states schema; sweeper will retry with backoff");
            return false;
        }
    }
}
