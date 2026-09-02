using Comuki.Engine.Orchestration.Infrastructure.Leases;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.Hosting;

/// <summary>
/// Hosted lease reaper: every <see cref="LeaseOptions.ReapInterval"/> it runs
/// one <see cref="LeaseReaper.ReapAsync"/> sweep in a fresh DI scope (the
/// DbContext is scoped). Reaper failures are not swallowed — an unhandled
/// sweep stops the host by the default BackgroundService behaviour. The
/// sweep runs as a named system consumer: it owns no subject, and the
/// scope query filters would otherwise hide the expired rows it reaps.
/// </summary>
/// <param name="scopeFactory"></param>
/// <param name="scopeAccessor"></param>
/// <param name="leaseOptions"></param>
/// <param name="logger"></param>
public sealed class LeaseReaperWorker(
    IServiceScopeFactory scopeFactory,
    ISubjectScopeAccessor scopeAccessor,
    IOptions<LeaseOptions> leaseOptions,
    ILogger<LeaseReaperWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            using var systemScope = scopeAccessor.AsSystem("lease-reaper");
            var reaper = scope.ServiceProvider.GetRequiredService<LeaseReaper>();

            var reaped = await reaper.ReapAsync(stoppingToken);
            if (reaped.Count > 0)
            {
                logger.LogInformation("Reaped {ReapedCount} expired work item lease(s)", reaped.Count);
            }

            await Task.Delay(leaseOptions.Value.ReapInterval, stoppingToken);
        }
    }
}
