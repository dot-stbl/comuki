using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Edition;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// <see cref="AddForEdition{TService}"/> —
/// per-service edition gating at the DI-registration boundary. Reads
/// the current <see cref="IEdition"/> once at composition time and
/// registers either the paid implementation or the Community
/// implementation, in contrast to <see cref="AddComukiModuleExtensions.AddComukiModule{TModule}"/>
/// which can leave a module entirely unregistered.
/// </summary>
public static class AddForEditionExtensions
{
    /// <summary>
    /// Per-service edition gating: registers <paramref name="use"/>
    /// when <paramref name="compositionEdition"/> covers
    /// <paramref name="paid"/>, otherwise <paramref name="otherwise"/>.
    /// Always registers SOMETHING — Community never lands in a
    /// half-built, throwing-stub state (issue #164 "one codebase, no
    /// data loss" open-core rule, E-series decisions).
    /// </summary>
    /// <typeparam name="TService">The service interface both candidates implement.</typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <param name="paid">The catalog feature the paid implementation requires.</param>
    /// <param name="use">The paid implementation type.</param>
    /// <param name="otherwise">The Community fallback implementation type.</param>
    /// <param name="compositionEdition">
    /// The composition-time <see cref="IEdition"/> snapshot the host built
    /// once via <see cref="Composition.CompositionEdition.Load"/> before the
    /// registration chain. Required — composition-time gating no longer
    /// builds a throwaway <see cref="IServiceProvider"/> to look up the
    /// runtime <see cref="IEdition"/> (<c>di-installer.md</c> §6 bans
    /// <c>services.BuildServiceProvider()</c> inside registration).
    /// </param>
    /// <param name="lifetime">DI lifetime; <see cref="ServiceLifetime.Singleton"/> matches every existing paid/Community interface-swap precedent (e.g. <c>IComputeProvider</c> in <c>ComputeInstaller</c>).</param>
    /// <exception cref="ArgumentException">
    /// Either <paramref name="use"/> or <paramref name="otherwise"/> is
    /// not assignable to <typeparamref name="TService"/>. The exception
    /// names which parameter and which type failed, raised BEFORE any
    /// registration happens so a misconfiguration fails at composition
    /// time, not at first resolve.
    /// </exception>
    /// <remarks>
    /// <para>
    /// HOT-RELOAD SEMANTICS — this check runs EXACTLY ONCE, at the
    /// moment <c>AddForEdition&lt;TService&gt;(...)</c> executes during
    /// host composition (effectively boot time). A .NET DI container is
    /// immutable once <c>WebApplicationBuilder.Build()</c> runs, so there
    /// is no way to swap a paid implementation in (or back out) after the
    /// fact.
    /// </para>
    /// <para>
    /// This is DIFFERENT from the request-time <see cref="IEdition.Has"/>
    /// reads the API gate (<see cref="RequiresFeatureFilter"/> /
    /// <see cref="RequiresFeatureMiddleware"/>) performs on every
    /// request — those hold a live <see cref="IEdition"/> and
    /// re-evaluate on every call because <c>IOptionsMonitor</c> reload is
    /// meaningful for a value read repeatedly at runtime. A DI
    /// <em>registration</em> decision has no such repeated read to hook a
    /// reload into.
    /// </para>
    /// <para>
    /// Consequence for operators: a license upgrade that newly unlocks
    /// the paid implementation requires a HOST RESTART to take effect.
    /// This is a real, intentional limitation, not an oversight.
    /// </para>
    /// <para>
    /// Unlike <see cref="AddComukiModuleExtensions.AddComukiModule{TModule}"/>
    /// (which can result in a module's services simply not being
    /// registered), this helper ALWAYS registers something — Community
    /// always gets a real, working <paramref name="otherwise"/>
    /// implementation, never a null/throwing stub.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddForEdition<TService>(
        this IServiceCollection services,
        Feature paid,
        Type use,
        Type otherwise,
        IEdition? compositionEdition = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TService : class
    {
        if (!typeof(TService).IsAssignableFrom(use))
        {
            throw new ArgumentException(
                $"'{use.FullName}' is not assignable to '{typeof(TService).FullName}'.",
                nameof(use));
        }

        if (!typeof(TService).IsAssignableFrom(otherwise))
        {
            throw new ArgumentException(
                $"'{otherwise.FullName}' is not assignable to '{typeof(TService).FullName}'.",
                nameof(otherwise));
        }

        // Per-service gating ALWAYS registers something (Community / paid
        // swap). The snapshot is the gate; a null snapshot on a gated
        // service swap is a wiring gap — fail loud at composition time.
        if (compositionEdition is null)
        {
            throw new InvalidOperationException(
                $"per-service edition swap for '{typeof(TService).FullName}' "
                + "was registered without a composition-time edition snapshot; "
                + "build the snapshot once via CompositionEdition.Load(builder.Configuration) "
                + "and pass it to every gated AddForEdition call.");
        }

        var implementationType = compositionEdition.Has(paid) ? use : otherwise;
        services.Add(new ServiceDescriptor(typeof(TService), implementationType, lifetime));
        return services;
    }
}
