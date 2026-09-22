using Comuki.Engine.Orchestration.Application.Handlers;
using Comuki.Engine.Orchestration.Application.MergeQueue;
using Comuki.Engine.Orchestration.Application.MergeQueue.Abandon;
using Comuki.Engine.Orchestration.Application.MergeQueue.Annotate;
using Comuki.Engine.Orchestration.Application.MergeQueue.BatchAbandon;
using Comuki.Engine.Orchestration.Application.MergeQueue.BatchClaim;
using Comuki.Engine.Orchestration.Application.MergeQueue.BatchMerge;
using Comuki.Engine.Orchestration.Application.MergeQueue.Claim;
using Comuki.Engine.Orchestration.Application.MergeQueue.MergeEntry;
using Comuki.Engine.Orchestration.Application.MergeQueue.Release;
using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Merge-queue (issue #11 sub-slice): enqueue-side validator + read service.
        services.AddSingleton<IValidator<EnqueueMergeRequestCommand>, MergeQueueValidator>();
        services.AddScoped<MergeQueueService>();

        // Per-verb action handlers — each command has its own validator so
        // every transition is independently validatable, authorizable and
        // auditable (replaces the previous enum-as-command dispatch).
        services.AddSingleton<IValidator<ClaimMergeQueueCommand>, ClaimMergeQueueValidator>();
        services.AddSingleton<IValidator<ReleaseMergeQueueCommand>, ReleaseMergeQueueValidator>();
        services.AddSingleton<IValidator<MergeMergeQueueCommand>, MergeMergeQueueValidator>();
        services.AddSingleton<IValidator<AbandonMergeQueueCommand>, AbandonMergeQueueValidator>();
        services.AddSingleton<IValidator<AnnotateMergeQueueCommand>, AnnotateMergeQueueValidator>();
        services.AddScoped<ClaimMergeQueueHandler>();
        services.AddScoped<ReleaseMergeQueueHandler>();
        services.AddScoped<MergeMergeQueueHandler>();
        services.AddScoped<AbandonMergeQueueHandler>();
        services.AddScoped<AnnotateMergeQueueHandler>();

        // Merge-batch read service (create + list only — actions per-verb).
        services.AddSingleton<IValidator<CreateMergeBatchCommand>, MergeBatchValidator>();
        services.AddScoped<MergeBatchService>();

        // Merge-batch per-verb action handlers.
        services.AddSingleton<IValidator<ClaimMergeBatchCommand>, ClaimMergeBatchValidator>();
        services.AddSingleton<IValidator<MergeMergeBatchCommand>, MergeMergeBatchValidator>();
        services.AddSingleton<IValidator<AbandonMergeBatchCommand>, AbandonMergeBatchValidator>();
        services.AddScoped<ClaimMergeBatchHandler>();
        services.AddScoped<MergeMergeBatchHandler>();
        services.AddScoped<AbandonMergeBatchHandler>();

        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
