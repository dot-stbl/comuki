using Comuki.Engine.Orchestration.Application.Handlers;
using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Engine.Orchestration.Application;

/// <summary>
/// Registration entry point for the orchestration application layer: status
/// machines, the claim handler and its validator. Hand-registered — no
/// assembly scanning.
/// </summary>
public static class OrchestrationApplicationExtensions
{
    /// <summary>Adds status machines, <see cref="ClaimWorkItemHandler"/> and its validator.</summary>
    /// <param name="services"></param>
    public static IServiceCollection AddOrchestrationApplication(this IServiceCollection services)
    {
        services.AddSingleton<RunStatusMachine>();
        services.AddSingleton<WorkItemStatusMachine>();
        services.AddSingleton<IValidator<ClaimWorkItemCommand>, ClaimWorkItemValidator>();
        services.AddScoped<ClaimWorkItemHandler>();

        return services;
    }
}
