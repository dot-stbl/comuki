using Comuki.Modules.Work.Application.Admission;
using Comuki.Modules.Work.Application.Assignments;
using Comuki.Modules.Work.Application.Cancellation;
using Comuki.Modules.Work.Application.Completion;
using Comuki.Modules.Work.Application.Decisions;
using Comuki.Modules.Work.Application.Dispatch;
using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Application.Sources;
using Comuki.Modules.Work.Infrastructure.Inbox;
using Comuki.Modules.Work.Infrastructure.Options;
using Comuki.Modules.Work.Infrastructure.Persistence;
using Comuki.Modules.Work.Infrastructure.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Work.Infrastructure;

/// <summary>
/// Composition glue for the Work bounded context — registers the
/// EF <see cref="WorkDbContext"/>, the four persistence ports
/// (task / inbox / binding / watermark), the option-binding
/// extension, and the application command handlers
/// (<see cref="AdmitTaskHandler"/> / <see cref="DispatchRunHandler"/>
/// / <see cref="CancelAttemptHandler"/>). Mirrors the
/// <c>AddIntegrationsPersistence</c> / <c>AddIntegrationsApplication</c>
/// split — persistence and handlers register in two extensions so a
/// test fixture can compose either side without booting both.
/// </summary>
public static class WorkModuleExtensions
{
    /// <summary>
    /// Registers <see cref="WorkDbContext"/>, the four EF store
    /// implementations, the option binding and the
    /// <c>WorkOptions</c> consumer. Call this before
    /// <see cref="AddWorkApplication"/>.
    /// </summary>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddWorkPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WorkDbContext>(
            (sp, builder) => WorkDbContext.ApplyOptions(
                builder,
                connectionString),
            optionsLifetime: ServiceLifetime.Singleton,
            contextLifetime: ServiceLifetime.Scoped);

        services.AddScoped<IWorkTaskStore, EfWorkTaskStore>();
        services.AddScoped<IWorkInbox, EfWorkInbox>();
        services.AddScoped<IInboundItemBindingStore, EfInboundItemBindingStore>();
        services.AddScoped<IWorkInboxWatermarkStore, EfWorkInboxWatermarkStore>();

        // di-options.md §5: every AddOptions chain ends with
        // .ValidateDataAnnotations().ValidateOnStart(). The
        // order matters — data-annotation validation runs first
        // (per-property constraints), then the validate-on-start
        // hook runs so a misconfigured section fails at boot rather
        // than at first read.
        services.AddOptions<WorkOptions>()
            .BindConfiguration(WorkOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers the Work application command handlers. Persistence
    /// must already be in the container.
    /// </summary>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddWorkApplication(this IServiceCollection services)
    {
        services.AddScoped<AdmitTaskHandler>();
        services.AddScoped<DispatchRunHandler>();
        services.AddScoped<CancelAttemptHandler>();

        // Decisions (tasks 5.x) — every Decision routes through
        // WorkDecideHandler; the per-kind branches are file-scoped
        // and registered so DI captures them rather than the dispatcher
        // itself.
        services.AddScoped<WorkDecideHandler>();
        services.AddScoped<IWorkDecisionKindHandler, RetryHandler>();
        services.AddScoped<IWorkDecisionKindHandler, ReplacementHandler>();
        services.AddScoped<IWorkDecisionKindHandler, WaiverHandler>();
        services.AddScoped<IWorkDecisionKindHandler, FailedResolutionHandler>();
        services.AddScoped<IWorkDecisionKindHandler, CancellationHandler>();

        // Completion (task 8.x) — Resolve handler + reviewer separation guard.
        services.AddScoped<WorkResolveHandler>();

        // Sources (task 7.1/7.2) — multi-source refs + primary change.
        services.AddScoped<ChangePrimarySourceHandler>();

        // Assignments (tasks 7.3/7.4/7.5) — actor metadata + proposal state machine.
        services.AddScoped<AssignTaskHandler>();
        services.AddScoped<ApproveAssignmentHandler>();

        return services;
    }
}
