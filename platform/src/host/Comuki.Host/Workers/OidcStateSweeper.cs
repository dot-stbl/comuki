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
}
