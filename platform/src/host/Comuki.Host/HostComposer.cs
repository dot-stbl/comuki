using Comuki.Host.Auth;
using Comuki.Host.Auth.Security;
using Comuki.Host.ControlPlane;
using Comuki.Host.Projects;
using Comuki.Modules.Identity.Application;
using Comuki.Modules.Identity.Infrastructure;
using Comuki.Modules.Identity.Infrastructure.Oidc;
using Comuki.Modules.Projects.Application;
using Comuki.Modules.Projects.Infrastructure;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Comuki.Host;

/// <summary>
/// The single composition point of the orchestrator host: services,
/// authentication schemes, controllers and the anonymous health endpoint.
/// <see cref="Program"/> resolves the database connection once through
/// <see cref="HostDatabase"/> and flows it in — for identity/projects here
/// and for the worker runtime wiring above the Compose call; integration
/// tests boot the exact same composition through this class on a test port.
/// </summary>
internal static class HostComposer
{
    /// <summary>Wires every host service and returns the built application, not yet started.</summary>
    /// <param name="builder"></param>
    /// <param name="database">Connection resolved once by <see cref="HostDatabase.Resolve"/>; flows into identity/projects persistence and the legacy-alias warning.</param>
    /// <returns></returns>
    public static WebApplication Compose(WebApplicationBuilder builder, HostDatabase.Connection database)
    {
        builder.Services.AddControlPlaneCatalogCore(builder.Configuration);

        builder.Services
            .AddIdentityApplication()
            .AddIdentityPersistence(database.ConnectionString)
            .AddIdentityAuth(builder.Configuration, typeof(HostComposer).Assembly);

        builder.Services.AddProjectsApplication();
        builder.Services.AddProjectsPersistence(database.ConnectionString);

        // Projects settings back the compute scale port (live-reload store
        // replaces the in-memory default registered by AddComukiCompute).
        builder.Services.AddSingleton<Engine.Compute.Ports.IProjectScaleSettings>(
            static serviceProvider => new ProjectScaleSettingsAdapter(
                serviceProvider.GetRequiredService<Modules.Projects.Application.Ports.IProjectSettingsStore>(),
                serviceProvider.GetRequiredService<IOptions<Engine.Compute.Options.ScaleSupervisorOptions>>()));

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

        HostDatabase.WarnLegacyAlias(database, app.Logger);

        app.UseExceptionHandler();
        app.UseAuthentication();

        app.MapGet(ApiRoutes.Health, static () => Results.Ok(new { status = "ok" }));
        app.MapControllers();
        app.MapProjectsEndpoints();

        return app;
    }
}
