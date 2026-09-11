using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Realtime.Broadcasting;
using Comuki.Host.Realtime.Reading;

namespace Comuki.Host.Realtime;

/// <summary>
/// Composition of the realtime surface (issue #7): the SignalR hub, the
/// journal broadcast interceptor on the orchestration context, and the
/// run→project lookup the joins and broadcasts share.
/// </summary>
public static class RealtimeExtensions
{
    /// <summary>
    /// Env-var name the realtime integration suite sets in
    /// <c>InitializeAsync</c> to opt back into detailed errors; production
    /// hosts never set it.
    /// </summary>
    internal const string DetailedErrorsEnvVar = "COMUKI_REALTIME_DETAILED_ERRORS";

    /// <summary>
    /// Wires the SignalR hub surface. <c>EnableDetailedErrors</c> is gated
    /// to <see cref="HostEnvironmentExtensions.IsDevelopment"/> so production
    /// containers never leak stack frames into <c>HubException</c> messages
    /// (security audit A05-1). The <see cref="DetailedErrorsEnvVar"/>
    /// opt-in survives as a support escape hatch; the historical
    /// <c>DOTNET_RUNNING_IN_CONTAINER</c> shortcut is intentionally
    /// removed — every .NET base image sets it, including production.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="environment">Hosting environment; the <c>IsDevelopment</c> gate lives here.</param>
    public static IServiceCollection AddComukiRealtime(this IServiceCollection services, IHostEnvironment environment)
    {
        var enableDetailedErrors = ShouldEnableDetailedErrors(environment);

        services.AddSignalR(options => options.EnableDetailedErrors = enableDetailedErrors);

        services.AddSingleton<IRunEventsBroadcaster, SignalRRunEventsBroadcaster>();
        services.AddSingleton<RunEventsBroadcastInterceptor>();
        services.AddScoped<IRealtimeRunProjects, RealtimeRunProjectsReader>();
        services.AddDbContext<OrchestrationDbContext>(static (serviceProvider, options) =>
            options.AddInterceptors(serviceProvider.GetRequiredService<RunEventsBroadcastInterceptor>()));

        return services;
    }

    /// <summary>
    /// True when the host runs in Development — resolved from
    /// <c>COMUKI_ENV</c> (with the quiet <c>ASPNETCORE_ENVIRONMENT</c> /
    /// <c>DOTNET_ENVIRONMENT</c> fallbacks, issue #54) — or when the
    /// dedicated <see cref="DetailedErrorsEnvVar"/> support knob is set.
    /// Production never reaches either branch.
    /// </summary>
    /// <param name="environment"></param>
    public static bool ShouldEnableDetailedErrors(IHostEnvironment environment)
    {
        return environment.IsDevelopment()
            || string.Equals(
                Environment.GetEnvironmentVariable(DetailedErrorsEnvVar),
                "true",
                StringComparison.Ordinal);
    }

    /// <summary>Maps the runs hub onto the app.</summary>
    /// <param name="app"></param>
    public static void MapComukiRealtime(this WebApplication app)
    {
        app.MapHub<RunsHub>(ApiRoutes.HubsRuns);
    }
}
