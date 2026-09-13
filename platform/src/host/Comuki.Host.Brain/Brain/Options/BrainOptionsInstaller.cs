using Microsoft.Extensions.Options;

namespace Comuki.Host.Brain.Brain.Options;

/// <summary>
/// Registers <see cref="BrainOptions"/> for the brain host — the same
/// options idiom <c>HostComposer</c> uses. The pipeline binds the
/// <c>[brain]</c> section and enforces the DataAnnotations caps
/// (<see cref="BrainOptions.GrpcPort"/>,
/// <see cref="BrainOptions.MaxToolIterations"/>) at startup; the
/// pre-build instance from <see cref="BrainOptions.Resolve(IConfiguration)"/>
/// (config-first precedence with the <c>COMUKI_BRAIN_MODEL_*</c> env
/// fallback — the same values Program.cs used for the Kestrel URL) is
/// pinned as the <see cref="IOptions{TOptions}"/> singleton consumers
/// inject, so a bare <c>AddSingleton(BrainOptions)</c> can never leave
/// consumers with an unbound default instance.
/// </summary>
public static class BrainOptionsInstaller
{
    /// <summary>Binds [brain] with startup validation and pins the resolved instance for <see cref="IOptions{TOptions}"/> consumers.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host's configuration (TOML, COMUKI_ env provider, appsettings).</param>
    /// <param name="resolved">Pre-build instance from <see cref="BrainOptions.Resolve(IConfiguration)"/> — the values Program.cs already read for the listen URL.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBrainOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        BrainOptions resolved)
    {
        services.AddOptions<BrainOptions>()
            .Bind(configuration.GetSection(BrainOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The pinned registration must stay on the IOptions<BrainOptions>
        // service type — a type-inferred AddSingleton would register the
        // concrete Options<T> and consumers would fall back to the
        // factory-bound instance (env-source precedence, not Resolve's
        // config-first contract).
        var pinned = Microsoft.Extensions.Options.Options.Create(resolved);
        services.AddSingleton(pinned);

        return services;
    }
}
