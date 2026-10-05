using System.Reflection;
using Comuki.Host.Runs;
using Comuki.Host.Runs.Controllers;
using Shouldly;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Steering seam (Phase 1a, openspec <c>add-orchestra</c>):
/// <c>IExecutionIdResolver</c> is the injection point between RunsController.SteerAsync
/// (the HTTP layer) and the worker-pipe layer that owns the live execution slot.
/// Today exactly one implementation (<c>ExecutionIdResolver</c>) ships in
/// <c>Comuki.Host</c>; Phase 1c adds a live-session-aware implementation that
/// talks to <c>IWorkerCommandPipe.TrySendInjectContext</c>.
/// <para>
/// This guard exists so a Phase 1c implementer who copies Phase 1a pattern by
/// accident (two resolvers, both registered) fails the build at PR time, not at
/// first-request DI resolution time. When 1c lands, the assertion flips to
/// <c>IsSuccessful =&gt; count is 1 OR 2</c> — or the seam moves to a
/// pre-registered discriminated union. Either way the test is the seam's
/// canonical place to assert its implementation count.
/// </para>
/// </summary>
public sealed class RunResolverIsSharedShould
{
    [Fact]
    public void HostAssemblyHasExactlyOneImplementationOfIExecutionIdResolver()
    {
        var hostAssembly = typeof(RunsController).Assembly;
        var interfaceType = typeof(IExecutionIdResolver);

        var implementers = hostAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && interfaceType.IsAssignableFrom(t))
            .ToList();

        implementers.ShouldNotBeEmpty(
            "Host assembly must have at least one IExecutionIdResolver implementation — today ExecutionIdResolver (Phase 1a).");

        implementers.Count.ShouldBe(
            1,
            $"Host assembly must have exactly one IExecutionIdResolver implementation; found {implementers.Count}: " +
            string.Join(", ", implementers.Select(x => x.FullName)) +
            ". Phase 1c will add the live-session-aware resolver; flip this guard then.");

        implementers[0].ShouldBeSameAs(typeof(ExecutionIdResolver));
    }

    [Fact]
    public void IExecutionIdResolverContractIsStable()
    {
        // Seal the contract: ResolveAsync(RunId, CancellationToken) → Task<WorkerId?>.
        // Phase 1c may add overloads (e.g. CancellationToken + injected flag), but
        // the existing signature is the seam the controller calls today.
        var resolveMethod = typeof(IExecutionIdResolver).GetMethod(
            "ResolveAsync",
            BindingFlags.Public | BindingFlags.Instance);

        resolveMethod.ShouldNotBeNull("IExecutionIdResolver.ResolveAsync is the public seam.");

        var parameters = resolveMethod.GetParameters();
        parameters.Length.ShouldBe(2);
        parameters[0].ParameterType.Name.ShouldBe("RunId");
        parameters[1].ParameterType.Name.ShouldBe("CancellationToken");

        var returnType = resolveMethod.ReturnType;
        returnType.IsGenericType.ShouldBeTrue("ResolveAsync returns Task<...>.");
        returnType.GetGenericTypeDefinition().ShouldBe(typeof(Task<>));
        var innerType = returnType.GetGenericArguments()[0];
        innerType.IsGenericType.ShouldBeTrue("ResolveAsync returns Task<WorkerId?>.");
        innerType.GetGenericTypeDefinition().ShouldBe(typeof(Nullable<>));
    }
}
