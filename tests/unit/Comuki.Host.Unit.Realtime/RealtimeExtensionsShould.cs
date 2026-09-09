using Comuki.Host.Realtime;
using Comuki.Host.Realtime.Broadcasting;
using Comuki.Host.Realtime.Reading;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>DI registration of the realtime surface.</summary>
public sealed class RealtimeExtensionsShould
{
    [Fact(DisplayName = "Given an empty service collection, when AddComukiRealtime is called in development, then broadcaster and reader are registered and SignalR detailed errors are on")]
    public void RegisterRealtimeServicesInDevelopment()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        _ = services.AddComukiRealtime(environment);

        services.Any(static descriptor => descriptor.ServiceType == typeof(IRunEventsBroadcaster)).ShouldBeTrue();
        services.Any(static descriptor => descriptor.ServiceType == typeof(RunEventsBroadcastInterceptor)).ShouldBeTrue();
        services.Any(static descriptor => descriptor.ServiceType == typeof(IRealtimeRunProjects)).ShouldBeTrue();

        var detailedErrors = ResolveEnableDetailedErrors(services);
        detailedErrors.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a production environment, when AddComukiRealtime is called, then SignalR detailed errors stay off so stack frames never reach the client")]
    public void DisableDetailedErrorsInProduction()
    {
        // Security audit A05-1: EnableDetailedErrors was on for every
        // production container because the DOTNET_RUNNING_IN_CONTAINER
        // shortcut fired unconditionally. Production must stay off
        // regardless of container detection.
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        _ = services.AddComukiRealtime(environment);

        var detailedErrors = ResolveEnableDetailedErrors(services);
        detailedErrors.ShouldBeFalse();
    }

    private static bool ResolveEnableDetailedErrors(IServiceCollection services)
    {
        var signalROptions = services.BuildServiceProvider()
            .GetRequiredService<IOptions<HubOptions>>()
            .Value;
        return signalROptions.EnableDetailedErrors.GetValueOrDefault();
    }
}
