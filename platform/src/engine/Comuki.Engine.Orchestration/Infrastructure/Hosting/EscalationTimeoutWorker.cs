using Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.Hosting;

/// <summary>
/// Hosted escalation-timeout ratchet: every
/// <see cref="EscalationTimeoutOptions.SweepInterval"/> it runs one
/// <see cref="EscalationTimeoutSweeper.SweepAsync"/> pass in a fresh DI
/// scope (the DbContext is scoped). Sweep failures are not swallowed — an
/// unhandled pass stops the host by the default BackgroundService
/// behaviour. The sweep runs as a named system consumer: it owns no
/// subject, and the scope query filters would otherwise hide the stale
/// Escalated rows it archives. When
/// <see cref="EscalationTimeoutOptions.Enabled"/> is false the worker
/// returns immediately and the loop never starts.
/// </summary>
/// <param name="scopeFactory"></param>
/// <param name="scopeAccessor"></param>
/// <param name="options"></param>
/// <param name="logger"></param>
public sealed class EscalationTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    ISubjectScopeAccessor scopeAccessor,
    IOptions<EscalationTimeoutOptions> options,
    ILogger<EscalationTimeoutWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Escalation timeout sweeper is disabled; worker not starting");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            using var systemScope = scopeAccessor.AsSystem("escalation-timeout-sweeper");
            var sweeper = scope.ServiceProvider.GetRequiredService<EscalationTimeoutSweeper>();

            var swept = await sweeper.SweepAsync(stoppingToken);
            if (swept.Archived > 0)
            {
                logger.LogInformation(
                    "Escalation-timeout sweeper archived {ArchivedCount} stale run(s)",
                    swept.Archived);
            }

            await Task.Delay(options.Value.SweepInterval, stoppingToken);
        }
    }
}
