using Comuki.Modules.Projects.Application;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Settings.Cache;
using Comuki.Modules.Projects.Application.Settings.DistributedCache;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Guards the exact DI shape a real host boot exercises for
/// <c>AddProjectsApplication</c>: ASP.NET Core's <c>ValidateOnBuild</c>
/// defaults to <c>true</c> in Development (<c>WebApplicationBuilder</c>
/// ties it to <c>IsDevelopment()</c>), so a developer running the host
/// locally with Redis disabled (the default — no <c>[redis]</c> section)
/// must still boot cleanly. <c>ValidateOnBuild</c> eagerly resolves the
/// constructor call site of every type-based registration
/// (<c>AddSingleton&lt;TConcrete&gt;()</c>) but treats a factory-based
/// registration (<c>AddSingleton(sp =&gt; ...)</c>) as opaque — confirmed
/// empirically (a type-registered stand-in with an unregistered
/// dependency throws <c>AggregateException</c> under
/// <c>ValidateOnBuild:true</c>; the same shape as a factory does not).
/// That is exactly why <c>AddProjectsApplication</c> registers
/// <see cref="DistributedProjectSettingsCache"/> — whose constructor needs
/// <see cref="IDistributedCache"/>, only ever registered when
/// <c>Redis:Enabled</c> is true — via a factory, not
/// <c>AddSingleton&lt;DistributedProjectSettingsCache&gt;()</c>.
/// </summary>
public sealed class DistributedProjectSettingsCacheRegistrationShould
{
    [Fact(DisplayName = "Given Redis is disabled (no IDistributedCache registered, the default), when AddProjectsApplication builds with ValidateOnBuild, then Build succeeds and the in-process cache resolves")]
    public void BuildSucceedsWhenDistributedCacheIsAbsent()
    {
        var services = NewServicesWithFakePorts();
        services.AddProjectsApplication();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        provider.GetRequiredService<IProjectSettingsSnapshotCache>().ShouldBeOfType<ProjectSettingsCache>();
    }

    [Fact(DisplayName = "Given Redis is enabled (IDistributedCache registered), when AddProjectsApplication builds with ValidateOnBuild, then Build succeeds and the distributed cache resolves")]
    public void BuildSucceedsAndPicksDistributedCacheWhenPresent()
    {
        var services = NewServicesWithFakePorts();
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())));
        services.AddProjectsApplication();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        provider.GetRequiredService<IProjectSettingsSnapshotCache>().ShouldBeOfType<DistributedProjectSettingsCache>();
    }

    /// <summary>
    /// Fakes the three persistence ports <c>AddProjectsApplication</c>'s
    /// handlers depend on — Infrastructure's own registrations are out of
    /// scope for this DI-shape test.
    /// </summary>
    private static IServiceCollection NewServicesWithFakePorts()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IProjectStore>());
        services.AddSingleton(Substitute.For<IProjectSettingsStore>());
        services.AddSingleton(Substitute.For<IDomainTypeAdmissionStore>());
        return services;
    }
}
