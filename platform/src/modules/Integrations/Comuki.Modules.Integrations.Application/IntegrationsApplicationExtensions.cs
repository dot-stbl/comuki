using Comuki.Modules.Integrations.Application.Inbox;
using Comuki.Modules.Integrations.Application.Options;
using Comuki.Modules.Integrations.Application.Sources;
using Comuki.Modules.Integrations.Application.Sync;
using Comuki.Modules.Integrations.Application.Tickets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Integrations.Application;

/// <summary>Registration entry point for the Integrations module application layer.</summary>
public static class IntegrationsApplicationExtensions
{
    /// <summary>
    /// Registers the webhook pipeline, the claim/native handlers, the
    /// inbox reader, the sources services and the provider registry. The
    /// run launcher, run status reader and the provider implementations
    /// are ports — the host composition (or a test) supplies them.
    /// </summary>
    public static IServiceCollection AddIntegrationsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<TicketProviderRegistry>();
        // Integrations handlers + services are Scoped: they each capture
        // IIntegrationsStore (Scoped — wraps IntegrationsDbContext) and, where noted,
        // IRunLauncher (also Scoped). Singletons here were a captive-
        // dependency bug; production boot with validateOnBuild: true
        // threw "Cannot consume scoped service from singleton" (audit
        // 2026-09-09). TicketProviderRegistry and SourceProbeService
        // stay Singleton — neither holds a Scoped dep.
        services.AddScoped<WebhookIngestionService>();
        services.AddScoped<ClaimInboundItemHandler>();
        services.AddScoped<CreateNativeInboundItemHandler>();
        services.AddScoped<InboxCatalogReader>();
        services.AddScoped<SourceConnectionService>();
        services.AddScoped<SecretRefResolverGuard>();
        services.AddScoped<AdmissionRuleService>();
        services.AddSingleton<SourceProbeService>();
        services.AddSingleton<IValidator<CreateNativeInboundItemCommand>, CreateNativeInboundItemValidator>();
        services.AddSingleton<IValidator<CreateSourceConnectionCommand>, CreateSourceConnectionValidator>();
        services.AddSingleton<IValidator<CreateAdmissionRuleCommand>, CreateAdmissionRuleValidator>();

        services.AddOptions<IntegrationsOptions>();

        return services;
    }
}
