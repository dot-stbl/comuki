using Comuki.Engine.Orchestration.Infrastructure.Hosting;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Leases;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Infrastructure.Queue;
using Comuki.Engine.Orchestration.Options;
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
    /// Wires the work item queue, run journal, lease reaper and the hosted
    /// reaper worker on top of <see cref="AddOrchestrationPersistence"/>.
    /// Bind the <c>Orchestration:Lease</c> section to tune the lease policy.
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

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IWorkItemQueue, WorkItemQueueEf>();
        services.AddScoped<IRunJournal, RunJournalEf>();
        services.AddScoped<LeaseReaper>();
        services.AddHostedService<LeaseReaperWorker>();

        return services;
    }
}
