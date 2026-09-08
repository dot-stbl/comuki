using System.Data.Common;
using Comuki.Modules.Identity.Application.Ports;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Workers;

/// <summary>
/// Hosted service that prunes the OIDC state table on a fixed interval.
/// The OIDC start handler issues 5-minute-TTL rows; the sweeper is the
/// bounded-leak defence for flows the operator abandoned (closed tab,
/// IdP timeout, browser back). Without this the table grows forever;
/// with it the table stays at <c>~ N_operators * one_in_flight</c>.
/// <para>
/// The store is scoped (one DbContext per unit of work), so each sweep
/// opens a fresh DI scope. Sweep failures are logged and retried on the
/// next interval — a transient DB error must NOT take the host down.
/// Shutdown cancels the loop via <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// Issue Q30 / v1.1: at startup the sweeper probes
/// <c>information_schema.tables</c> for the <c>oidc_states</c> table. A
/// fresh deploy whose migrator has not yet run lands here, the probe
/// returns <c>false</c>, and the sweeper logs <c>Critical</c> with the
/// exact remediation message. The host does NOT refuse to start — the
/// loop continues and the probe repeats on every cycle until the
/// migrator lands, so an operator who runs the migrator mid-flight sees
/// the next probe flip to <c>true</c> and the sweeper quietly begin
/// work without a restart.
/// </para>
/// </summary>
/// <param name="scopeFactory">DI scope factory — the store is scoped.</param>
/// <param name="options">Bound from <c>Host:OidcSweep</c>.</param>
/// <param name="clock">Injected to keep the cutoff testable.</param>
/// <param name="logger">Structured logger — Information on count, Warning on transient failure.</param>
public sealed class OidcStateSweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<OidcSweepOptions> options,
    TimeProvider clock,
    ILogger<OidcStateSweeper> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("OIDC state sweep is disabled (Host:OidcSweep:Enabled=false)");
            return;
        }

        var interval = options.Value.Interval;

        // Q30 / v1.1: probe once at startup so a missing-table failure
        // is loud at boot, not silent at the first sweep. We do not
        // refuse to start — the loop below re-checks on every cycle.
        await ProbeSchemaOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is DbException or IOException or TimeoutException)
            {
                logger.LogWarning(exception, "oidc state sweep failed; retrying next interval");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Runs one sweep now — also the test entry point.</summary>
    /// <param name="cancellationToken"></param>
    public async Task SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOidcStateStore>();

        var cutoff = clock.GetUtcNow() - options.Value.StateTtl;
        var deleted = await store.DeleteExpiredAsync(cutoff, cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Swept {SweptCount} expired OIDC state row(s)", deleted);
        }
    }

    /// <summary>
    /// Q30 / v1.1: ask the store whether its table is actually present
    /// in the database the migrator populated. Logs <c>Critical</c> with
    /// a remediation hint when the probe returns <c>false</c>. Never
    /// throws — the host starts and the loop above re-probes on every
    /// cycle.
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task ProbeSchemaOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IOidcStateStore>();

            var tableExists = await store.TableExistsAsync(cancellationToken);
            if (tableExists)
            {
                return;
            }

            logger.LogCritical(
                "migrator not run, oidc_states table missing; sweeper will retry on each cycle. Run the migrator (see .agents/docs/operations/runbook.md) and the next sweep will succeed without a host restart.");
        }
        catch (Exception exception) when (exception is OperationCanceledException or DbException or IOException or TimeoutException)
        {
            logger.LogWarning(
                exception,
                "could not probe oidc_states schema at startup; sweeper will retry on each cycle");
        }
    }
}
