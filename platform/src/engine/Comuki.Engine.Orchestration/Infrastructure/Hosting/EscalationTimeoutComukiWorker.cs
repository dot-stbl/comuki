using Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.Hosting;

/// <summary>
/// Escalation-timeout ratchet behind the comuki worker registry: every
/// <see cref="EscalationTimeoutOptions.SweepInterval"/> it runs one
/// <see cref="EscalationTimeoutSweeper.SweepAsync"/> pass against the
/// registry's per-cycle scope (the DbContext is scoped). Sweep failures are
/// reported to the registry, which logs and retries with backoff. The sweep
/// runs as a named system consumer: it owns no subject, and the scope query
/// filters would otherwise hide the stale Escalated rows it archives. When
/// <see cref="EscalationTimeoutOptions.Enabled"/> is false the installer
/// skips the registration entirely (the oidc-sweep pattern).
/// </summary>
/// <param name="scopeAccessor">Installs the system scope the sweep needs to see every subject's rows.</param>
/// <param name="options">Bound from <c>Orchestration:EscalationTimeout</c>; the sweep interval drives the schedule.</param>
/// <param name="logger">Structured logger — Information on a non-zero count.</param>
public sealed class EscalationTimeoutComukiWorker(
    ISubjectScopeAccessor scopeAccessor,
    IOptions<EscalationTimeoutOptions> options,
    ILogger<EscalationTimeoutComukiWorker> logger) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "escalation-timeout";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(options.Value.SweepInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        using var systemScope = scopeAccessor.AsSystem(Name);
        var sweeper = context.Services.GetRequiredService<EscalationTimeoutSweeper>();

        var swept = await sweeper.SweepAsync(cancellationToken);
        if (swept.Archived > 0)
        {
            logger.LogInformation(
                "Escalation-timeout sweeper archived {ArchivedCount} stale run(s)",
                swept.Archived);
        }

        return WorkerResult.Ok($"archived {swept.Archived}", swept.Archived);
    }
}
