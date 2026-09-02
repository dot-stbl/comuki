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

    /// <param name="services"></param>
    public static IServiceCollection AddComukiRealtime(this IServiceCollection services)
    {
        // SignalR detailed errors carry stack frames in HubException
        // messages — a real attack surface (issue #19). Default off in
        // production; enable only for explicit diagnostics: dev hosts run
        // under ASPNETCORE_ENVIRONMENT=Development, container operators
        // set DOTNET_RUNNING_IN_CONTAINER, and the integration suite flips
        // the dedicated test-only env var on for its lifetime.
        var enableDetailedErrors = ShouldEnableDetailedErrors();
        services.AddSignalR(options => options.EnableDetailedErrors = enableDetailedErrors);

        services.AddSingleton<IRunEventsBroadcaster, SignalRRunEventsBroadcaster>();
        services.AddSingleton<RunEventsBroadcastInterceptor>();
        services.AddScoped<IRealtimeRunProjects, RealtimeRunProjectsReader>();
        services.AddDbContext<OrchestrationDbContext>(static (serviceProvider, options) =>
            options.AddInterceptors(serviceProvider.GetRequiredService<RunEventsBroadcastInterceptor>()));

        return services;
    }

    /// <summary>True when one of the three diagnostic opt-ins is set; false otherwise.</summary>
    private static bool ShouldEnableDetailedErrors()
    {
        return string.Equals(
                   Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                   "Development",
                   StringComparison.Ordinal)
            || string.Equals(
                   Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                   "Development",
                   StringComparison.Ordinal)
            || string.Equals(
                   Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                   "true",
                   StringComparison.Ordinal)
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
