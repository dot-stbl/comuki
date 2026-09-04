using Comuki.Modules.Intake.Application.Inbox;
using Comuki.Modules.Intake.Application.Options;
using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Application.Sync;
using Comuki.Modules.Intake.Application.Tickets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Intake.Application;

/// <summary>Registration entry point for the Intake module application layer.</summary>
public static class IntakeApplicationExtensions
{
    /// <summary>
    /// Registers the webhook pipeline, the claim/native handlers, the
    /// inbox reader, the sources services and the provider registry. The
    /// run launcher, run status reader and the provider implementations
    /// are ports — the host composition (or a test) supplies them.
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddIntakeApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<TicketProviderRegistry>();
        services.AddSingleton<WebhookIntakeService>();
        services.AddSingleton<ClaimTicketHandler>();
        services.AddSingleton<CreateNativeTicketHandler>();
        services.AddSingleton<InboxCatalogReader>();
        services.AddSingleton<SourceConnectionService>();
        services.AddSingleton<AdmissionRuleService>();
        services.AddSingleton<SourceProbeService>();
        services.AddSingleton<IValidator<CreateNativeTicketCommand>, CreateNativeTicketValidator>();
        services.AddSingleton<IValidator<CreateSourceConnectionCommand>, CreateSourceConnectionValidator>();
        services.AddSingleton<IValidator<CreateAdmissionRuleCommand>, CreateAdmissionRuleValidator>();

        services.AddOptions<IntakeOptions>();

        return services;
    }
}
