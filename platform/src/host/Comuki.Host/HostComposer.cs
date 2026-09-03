using Comuki.Host.Artifacts;
using Comuki.Host.Auth;
using Comuki.Host.Auth.Security;
using Comuki.Host.Chat.Brain;
using Comuki.Host.Chat.RunStarter;
using Comuki.Host.Chat.Sessions;
using Comuki.Host.Chat.Tools;
using Comuki.Host.ControlPlane;
using Comuki.Host.Costs;
using Comuki.Host.Errors;
using Comuki.Host.Intake;
using Comuki.Host.Projects;
using Comuki.Host.Realtime;
using Comuki.Modules.Artifacts.Application;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Modules.Artifacts.Infrastructure;
using Comuki.Modules.Chat.Application;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Infrastructure;
using Comuki.Modules.Costs.Application;
using Comuki.Modules.Costs.Infrastructure;
using Comuki.Modules.Identity.Application;
using Comuki.Modules.Identity.Infrastructure;
using Comuki.Modules.Identity.Infrastructure.Oidc;
using Comuki.Modules.Intake.Application;
using Comuki.Modules.Intake.Application.Options;
using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Infrastructure;
using Comuki.Modules.Projects.Application;
using Comuki.Modules.Projects.Infrastructure;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Contracts.Memory;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Telemetry.Installers;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // Telemetry first: options ValidateOnStart always; OTLP SDK only when
        // Telemetry:OtlpEndpoint is set (see deploy/README — VictoriaMetrics :8431).
        builder.Services.AddComukiTelemetry(builder.Configuration);

        builder.Services.AddControlPlaneCatalogCore(builder.Configuration);

        builder.Services
            .AddIdentityApplication()
            .AddIdentityPersistence(database.ConnectionString)
            .AddIdentityAuth(builder.Configuration, typeof(HostComposer).Assembly);

        builder.Services.AddProjectsApplication();
        builder.Services.AddProjectsPersistence(database.ConnectionString);

        // Costs module (S9 T9.5): usage_events + budgets. Budget ports are
        // host-composed (Projects settings + orchestration cancel/journal)
        // and registered BEFORE AddCostsApplication so its TryAdd defaults
        // stay out of the way.
        builder.Services.AddSingleton<IProjectBudgetSettings, ProjectBudgetSettingsAdapter>();
        builder.Services.AddScoped<IBudgetGate, OrchestrationBudgetGate>();
        builder.Services.AddCostsApplication();
        builder.Services.AddCostsPersistence(database.ConnectionString);

        // Chat module (issue #5 slice B): turn services + Voluta graph over
        // the chat schema. The brain port falls back to the in-process stub
        // and the memory digest to the empty fallback until the brain host
        // and the memory store slices land — TryAdd keeps the real
        // implementations winning once registered. The tool executor scopes
        // into orchestration, which Program wires above this call.
        builder.Services
            .AddChatApplication()
            .AddChatPersistence(database.ConnectionString);
        builder.Services.TryAddSingleton<IBrainClient, BrainStub>();
        builder.Services.TryAddSingleton<IMemoryDigest, EmptyMemoryDigest>();
        builder.Services.AddSingleton<IChatToolExecutor, HostChatToolExecutor>();
        builder.Services.AddSingleton<ChatSessionResolver>();
        builder.Services.AddScoped<IRunsReader, OrchestrationRunsReader>();
        builder.Services.AddScoped<Runs.RunsListHandler>();
        builder.Services.AddScoped<ChatRunStarter>();
        builder.Services.AddOptions<ChatWorkerDefaults>()
            .Bind(builder.Configuration.GetSection(ChatWorkerDefaults.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Intake module (issue #6): webhooks + native tickets over the
        // intake schema, the tracker Refit providers, and the run status
        // bridge worker. Runs launch through the host-composed port
        // (IntakeRunLauncher) — the module never references the engine;
        // bridge intervals are bound from Intake:* configuration.
        builder.Services
            .AddIntakeApplication()
            .AddIntakePersistence(database.ConnectionString)
            .AddIntakeProviders();
        builder.Services.AddOptions<IntakeOptions>()
            .Bind(builder.Configuration.GetSection(IntakeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddScoped<IRunLauncher, IntakeRunLauncher>();
        builder.Services.AddScoped<IRunStatusReader, OrchestrationRunStatusReader>();
        builder.Services.AddOptions<IntakeWorkerDefaults>()
            .Bind(builder.Configuration.GetSection(IntakeWorkerDefaults.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Artifacts module (issue #28): MinIO-backed run bundle store +
        // the polling packager driver. The two adapters
        // (IRunArtifactJournalSource, IRunArtifactRunSource) live in the
        // host composition root so the artifacts module never reaches
        // into the engine schema — they read runs / work items through
        // the orchestration DbContext that Program already wired above.
        // The host driver wraps the in-module polling helper and emits
        // a `run.artifacts_bundled` journal event after each bundle.
        builder.Services.AddArtifactsApplication();
        builder.Services.AddArtifactsPersistence(database.ConnectionString, builder.Configuration);
        builder.Services.AddScoped<IRunArtifactJournalSource, OrchestrationArtifactJournalSource>();
        builder.Services.AddScoped<IRunArtifactRunSource, OrchestrationArtifactRunSource>();
        builder.Services.AddHostedService<RunArtifactPackagerHostService>();

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
        builder.Services.AddExceptionHandler<ProviderExceptionHandler>();

        // Realtime surface (issue #7): SignalR hub + the journal broadcast
        // interceptor. Registered after orchestration persistence — the
        // interceptor appends to the context options the engine registered
        // (Program wires persistence before Compose; the chat test fixture
        // mirrors that order).
        builder.Services.AddComukiRealtime();

        // Ambient subject scope: one AsyncLocal-backed accessor for the
        // whole process — the middleware installs a scope per request, the
        // worker surfaces and hosted consumers declare AsSystem, and the
        // context scope members read it inside the query filters.
        builder.Services.AddSingleton<Shared.Kernel.Scoping.ISubjectScopeAccessor, Shared.Kernel.Scoping.AsyncLocalSubjectScopeAccessor>();

        var app = builder.Build();

        HostDatabase.WarnLegacyAlias(database, app.Logger);

        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<SubjectScopeMiddleware>();

        app.MapGet(ApiRoutes.Health, static () => Results.Ok(new { status = "ok" }));
        app.MapControllers();
        app.MapProjectsEndpoints();
        app.MapCostsEndpoints();
        app.MapComukiRealtime();

        return app;
    }
}
