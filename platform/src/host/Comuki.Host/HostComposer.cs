using Comuki.Host.Auth;
using Comuki.Host.Auth.Security;
using Comuki.Host.ControlPlane;
using Comuki.Modules.Identity.Application;
using Comuki.Modules.Identity.Infrastructure;
using Comuki.Modules.Identity.Infrastructure.Oidc;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Comuki.Host;

/// <summary>
/// The single composition point of the orchestrator host: services,
/// authentication schemes, controllers and the anonymous health endpoint.
/// <see cref="Program"/> stays a four-line entry; integration tests boot
/// the exact same composition through this class on a test port.
/// </summary>
internal static class HostComposer
{
    /// <summary>Wires every host service and returns the built application, not yet started.</summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static WebApplication Compose(WebApplicationBuilder builder)
    {
        var connectionString = HostDatabase.ResolveConnectionString(builder.Configuration);

        builder.Services.AddControlPlaneCatalogCore(builder.Configuration);

        builder.Services
            .AddIdentityApplication()
            .AddIdentityPersistence(connectionString)
            .AddIdentityAuth(builder.Configuration, typeof(HostComposer).Assembly);

        // The /auth/oidc/{provider}/start surface reads the configured
        // provider list for its 404s; the ticket event + callback path
        // rewrite below turn the module's OIDC schemes into local-cookie
        // logins through OidcAccountLinker.
        builder.Services.AddOptions<OidcOptions>()
            .Bind(builder.Configuration.GetSection(OidcOptions.SectionName));
        builder.Services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, OidcLoginPostConfigure>();

        builder.Services.AddSingleton(BootstrapAdminOptions.Resolve(builder.Configuration));
        builder.Services.AddScoped<BootstrapAdminSeeder>();
        builder.Services.AddHostedService<BootstrapAdminStartupService>();

        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseAuthentication();

        app.MapGet(ApiRoutes.Health, static () => Results.Ok(new { status = "ok" }));
        app.MapControllers();

        return app;
    }
}

/// <summary>Connection-string resolution shared by the host and its test boot: <c>COMUKI_DB</c> env, then <c>ConnectionStrings:Comuki</c>.</summary>
file static class HostDatabase
{
    public const string ConnectionStringName = "Comuki";

    public const string EnvVariable = "COMUKI_DB";

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var fromConfiguration = configuration.GetConnectionString(ConnectionStringName);

        return string.IsNullOrWhiteSpace(fromConfiguration)
            ? throw new InvalidOperationException(
                $"connection string not found: set the {EnvVariable} env var or ConnectionStrings:{ConnectionStringName} in configuration")
            : fromConfiguration;
    }
}
