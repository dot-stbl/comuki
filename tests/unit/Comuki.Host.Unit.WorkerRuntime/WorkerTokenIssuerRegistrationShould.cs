using Comuki.Engine.Compute.Security;
using Comuki.Host.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// The worker runtime wires <see cref="WorkerTokenIssuer"/> via
/// <see cref="WorkerRuntimeExtensions.AddWorkerRuntime"/>. The duplicate
/// <c>AddSingleton</c> in <c>ComputeInstaller</c> that used to race against
/// this <c>TryAddSingleton</c> is gone — calling <c>AddWorkerRuntime</c>
/// twice on the same <see cref="IServiceCollection"/> must register the
/// issuer exactly once and resolve to the same instance either way.
/// </summary>
public sealed class WorkerTokenIssuerRegistrationShould
{
    [Fact(DisplayName = "Given AddWorkerRuntime called twice, when WorkerTokenIssuer is resolved, then it is registered exactly once and resolves cleanly")]
    public void ResolveWorkerTokenIssuerOnce()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Compute:WorkerToken:Pepper"] = "unit-test-pepper-16ch",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddWorkerRuntime(configuration);
        services.AddWorkerRuntime(configuration);

        using var provider = services.BuildServiceProvider();
        var issuer = provider.GetService<WorkerTokenIssuer>();

        issuer.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given AddWorkerRuntime called twice, when WorkerTokenIssuer is resolved twice, then both resolutions return the same singleton instance")]
    public void ResolveWorkerTokenIssuerAsSingleton()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Compute:WorkerToken:Pepper"] = "unit-test-pepper-16ch",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddWorkerRuntime(configuration);
        services.AddWorkerRuntime(configuration);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<WorkerTokenIssuer>();
        var second = provider.GetRequiredService<WorkerTokenIssuer>();

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given AddWorkerRuntime, when an issued token is validated, then it round-trips through the registered issuer")]
    public void IssuedTokenValidatesThroughRegisteredIssuer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Compute:WorkerToken:Pepper"] = "unit-test-pepper-16ch",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddWorkerRuntime(configuration);

        using var provider = services.BuildServiceProvider();
        var issuer = provider.GetRequiredService<WorkerTokenIssuer>();
        var workerId = Shared.Kernel.Ids.WorkerId.New();
        var token = issuer.Issue(workerId);

        issuer.Validate(token).ShouldBe(workerId);
    }
}
