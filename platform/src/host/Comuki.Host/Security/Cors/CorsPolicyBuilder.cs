using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Comuki.Host.Security.Cors;

/// <summary>
/// Builds the named CORS policy the host applies to the versioned API
/// surface (<c>AddCors(name).AllowCredentials().WithOrigins(...)</c>).
/// Cookie-authenticated browser clients need <c>AllowCredentials</c>;
/// API-key callers are bearer-token only and never trip the CORS
/// preflight — <c>*</c> with credentials is illegal per spec, the
/// allow-list is the only correct value.
/// <para>
/// <see cref="CorsPolicyNames.Dashboard"/> is the named policy. The
/// builder throws when the resolved options pair
/// <see cref="ComukiCorsOptions.AllowWildcard"/> with a
/// <c>Production</c> hosting environment — wildcards leak credentials.
/// </para>
/// </summary>
public static class ComukiCorsInstaller
{
    /// <summary>Configures CORS, validates options, and binds the named policy on the service collection.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">Configuration root — binds <see cref="ComukiCorsOptions"/> from <c>Host:Cors</c>.</param>
    /// <param name="environment">Hosting environment; the builder reads <see cref="IHostEnvironment.IsProduction"/> for the wildcard gate.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddComukiCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration.GetSection(ComukiCorsOptions.SectionName).Get<ComukiCorsOptions>() ?? new ComukiCorsOptions();

        if (options.AllowWildcard && environment.IsProduction())
        {
            throw new InvalidOperationException(
                $"{ComukiCorsOptions.SectionName}:allowWildcard=true is forbidden in Production; "
                + "configure an explicit allowedOrigins list or run with ASPNETCORE_ENVIRONMENT=Development");
        }

        services.AddCors(cors => cors.AddPolicy(
            CorsPolicyNames.Dashboard,
            policy => BuildPolicy(policy, options)));

        return services;
    }

    /// <summary>
    /// Applies the named policy to one <see cref="CorsPolicyBuilder"/>.
    /// Pure function — exposed for the unit suite that exercises the
    /// shape of the policy the host installs without spinning up the
    /// full <c>AddCors</c> plumbing.
    /// </summary>
    /// <param name="builder">The framework-side policy builder.</param>
    /// <param name="options">The resolved <see cref="ComukiCorsOptions"/>.</param>
    public static void BuildPolicy(CorsPolicyBuilder builder, ComukiCorsOptions options)
    {
        builder
            .WithHeaders([.. options.AllowedHeaders])
            .WithMethods([.. options.AllowedMethods])
            .AllowCredentials();

        if (options.AllowWildcard)
        {
            builder.SetIsOriginAllowed(static _ => true);
        }
        else
        {
            builder.WithOrigins(options.AllowedOrigins);
        }
    }
}

/// <summary>Named CORS policies registered by <see cref="ComukiCorsInstaller"/>.</summary>
public static class CorsPolicyNames
{
    /// <summary>Dashboard SPA + API-key cross-origin callers.</summary>
    public const string Dashboard = "comuki.dashboard";
}
