using Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;
using Comuki.Engine.Orchestration.Infrastructure.Hosting;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Leases;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;
using Comuki.Engine.Orchestration.Infrastructure.Queue;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Contracts.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Engine.Orchestration.Infrastructure;

/// <summary>Registration entry point for orchestration persistence.</summary>
public static class OrchestrationInfrastructureExtensions
{
    /// <summary>
    /// Registers <see cref="OrchestrationDbContext"/> (Npgsql + snake_case
    /// via <see cref="OrchestrationDbContext.ApplyOptions"/>). Scoped — one
    /// context per unit of work / request.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    public static IServiceCollection AddOrchestrationPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OrchestrationDbContext>(options =>
            OrchestrationDbContext.ApplyOptions(options, connectionString));
        return services;
    }

    /// <summary>
    /// Wires the work item queue, run journal, lease reaper and the
    /// escalation-timeout sweeper on top of
    /// <see cref="AddOrchestrationPersistence"/>. Bind the
    /// <c>Orchestration:Lease</c> section to tune the lease policy and
    /// the <c>Orchestration:EscalationTimeout</c> section to tune the
    /// passive autonomy ratchet on the Escalated run state. The reaper and
    /// the sweeper register as <see cref="IComukiWorker"/>s — a host that
    /// runs them must also call <c>AddComukiWorkers()</c>.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddOrchestrationQueue(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LeaseOptions>()
            .Bind(configuration.GetSection(LeaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EscalationTimeoutOptions>()
            .Bind(configuration.GetSection(EscalationTimeoutOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IWorkItemQueue, WorkItemQueueEf>();
        services.AddScoped<IRunJournal, RunJournalEf>();
        services.AddScoped<IMergeQueueStore, MergeQueueStoreEf>();
        services.AddScoped<IMergeBatchStore, MergeBatchStoreEf>();
        services.AddScoped<LeaseReaper>();
        services.AddSingleton<IComukiWorker, LeaseReaperComukiWorker>();
        services.AddScoped<EscalationTimeoutSweeper>();

        // EscalationTimeout:Enabled=false skips the worker registration
        // entirely (the same pattern the host uses for oidc-sweep) — the
        // registry collects its workers at Build, so the kill-switch is
        // resolved synchronously from the bound section here.
        var escalationEnabled = configuration.GetSection(EscalationTimeoutOptions.SectionName)
            .Get<EscalationTimeoutOptions>()?.Enabled ?? true;
        if (escalationEnabled)
        {
            services.AddSingleton<IComukiWorker, EscalationTimeoutComukiWorker>();
        }

        return services;
    }
}
