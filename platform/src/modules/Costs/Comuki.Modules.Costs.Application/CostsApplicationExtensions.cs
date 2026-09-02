using Comuki.Modules.Costs.Application.Aggregation;
using Comuki.Modules.Costs.Application.Budgets;
using Comuki.Modules.Costs.Application.Queries;
using Comuki.Modules.Costs.Application.Recording;
using Comuki.Shared.Contracts.Costs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Costs.Application;

/// <summary>Composition of the Costs application layer.</summary>
public static class CostsApplicationExtensions
{
    /// <summary>
    /// Registers Costs application services (recorder, aggregator, queries).
    /// Budget ports default to unlimited / no-op; the host replaces them with
    /// Projects settings + cancel/journal adapters via <c>AddSingleton</c>
    /// before or after this call (<see cref="TryAddSingleton{TService,TImplementation}"/>).
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddCostsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IBudgetGate, NullBudgetGate>();
        services.TryAddSingleton<IProjectBudgetSettings, UnlimitedBudgetSettings>();
        _ = services.AddScoped<IUsageRecorder, UsageRecorder>();
        _ = services.AddScoped<RunCostAggregator>();
        _ = services.AddScoped<GetProjectCostsHandler>();
        return services;
    }
}
