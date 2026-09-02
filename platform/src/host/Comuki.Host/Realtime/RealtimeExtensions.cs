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
    /// Registers SignalR (default System.Text.Json protocol) and the
    /// broadcast pipeline. The interceptor attaches through a second
    /// <c>AddDbContext</c> configuration — EF Core combines every options
    /// action registered for a context, so the engine's
    /// <c>AddOrchestrationPersistence</c> recipe stays untouched and this
    /// call only appends the interceptor.
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddComukiRealtime(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IRunEventsBroadcaster, SignalRRunEventsBroadcaster>();
        services.AddSingleton<RunEventsBroadcastInterceptor>();
        services.AddScoped<IRealtimeRunProjects, RealtimeRunProjectsReader>();
        services.AddDbContext<OrchestrationDbContext>(static (serviceProvider, options) =>
            options.AddInterceptors(serviceProvider.GetRequiredService<RunEventsBroadcastInterceptor>()));

        return services;
    }

    /// <summary>Maps the runs hub onto the app.</summary>
    /// <param name="app"></param>
    public static void MapComukiRealtime(this WebApplication app)
    {
        _ = app.MapHub<RunsHub>(ApiRoutes.HubsRuns);
    }
}
