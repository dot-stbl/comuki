using Comuki.Host.Realtime;
using Comuki.Host.Realtime.Broadcasting;
using Comuki.Host.Realtime.Reading;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>DI registration of the realtime surface.</summary>
public sealed class RealtimeExtensionsShould
{
    [Fact(DisplayName = "Given an empty service collection, when AddComukiRealtime is called, then broadcaster and reader are registered")]
    public void RegisterRealtimeServices()
    {
        var services = new ServiceCollection();

        _ = services.AddComukiRealtime();

        services.Any(static descriptor => descriptor.ServiceType == typeof(IRunEventsBroadcaster)).ShouldBeTrue();
        services.Any(static descriptor => descriptor.ServiceType == typeof(RunEventsBroadcastInterceptor)).ShouldBeTrue();
        services.Any(static descriptor => descriptor.ServiceType == typeof(IRealtimeRunProjects)).ShouldBeTrue();
    }
}
