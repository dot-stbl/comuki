using Comuki.Host.Artifacts;
using Comuki.Host.Auth;
using Comuki.Host.Auth.Models;
using Comuki.Host.Auth.Security;
using Comuki.Host.Chat.Brain;
using Comuki.Host.Chat.RunStarter;
using Comuki.Host.Chat.Sessions;
using Comuki.Host.Chat.Tools;
using Comuki.Host.ControlPlane;
using Comuki.Host.Costs;
using Comuki.Host.Errors;
using Comuki.Host.HealthChecks;
using Comuki.Host.Intake;
using Comuki.Host.Knowledge;
using Comuki.Host.Mcp;
using Comuki.Host.Projects;
using Comuki.Host.Proxy;
using Comuki.Host.Realtime;
using Comuki.Host.Runs;
using Comuki.Host.Scheduler;
using Comuki.Host.Security.Cors;
using Comuki.Host.Security.ProductionSecrets;
using Comuki.Host.Security.RateLimit;
using Comuki.Host.Workers;
using Comuki.Modules.Artifacts.Application;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Comuki.Modules.Artifacts.Infrastructure;
using Comuki.Modules.Chat.Application;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Infrastructure;
using Comuki.Modules.Costs.Application;
using Comuki.Modules.Costs.Infrastructure;
using Comuki.Modules.Identity.Application;
using Comuki.Modules.Identity.Application.Oidc;
using Comuki.Modules.Identity.Infrastructure;
using Comuki.Modules.Intake.Application;
using Comuki.Modules.Intake.Application.Options;
using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Infrastructure;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure;
using Comuki.Modules.Projects.Application;
using Comuki.Modules.Projects.Infrastructure;
using Comuki.Modules.Proxy.Application;
using Comuki.Modules.Proxy.Infrastructure;
using Comuki.Modules.Scheduler.Application;
using Comuki.Modules.Scheduler.Application.Options;
using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Infrastructure;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Contracts.Memory;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Secrets;
using Comuki.Shared.Telemetry.Installers;
using FluentValidation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VaultSharp;
using static Comuki.Host.VaultSecretClientFactory;
using VersionResponse = Comuki.Host.Versioning.VersionResponse;

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
    /// <param name="builder">The host builder whose services and middleware this call composes.</param>
    /// <param name="database">Connection resolved once by <see cref="HostDatabase.Resolve"/>; flows into identity/projects persistence and the legacy-alias warning.</param>
    /// <returns>The composed, not-yet-started <see cref="WebApplication"/>.</returns>
    /// <remarks>
    /// DI scope/build validation runs unconditionally — every consumer of
    /// this composition (production boot and integration tests) gets the
    /// production-safe defaults. A captive-singleton regression fails the
    /// boot at <c>Build()</c> time, in every environment. The flag used to
    /// live here as an escape hatch for tests; that hatch is gone — see the
    /// DI-lifetime audit (2026-09-09) for the rationale.
    /// </remarks>
    public static WebApplication Compose(WebApplicationBuilder builder, HostDatabase.Connection database)
    {
        // Telemetry first: options ValidateOnStart always; OTLP SDK only when
        // Telemetry:OtlpEndpoint is set (see deploy/README — VictoriaMetrics :8431).
        builder.Services.AddComukiTelemetry(builder.Configuration);

        // Secret-resolution subsystem (issue #52, slice 1): the env /
        // file providers are always registered (cheap, no I/O at boot);
        // the file provider short-circuits on [Secrets:File]:Enabled
        // at resolve time, so an opted-out deployment never touches the
        // filesystem. The composite resolver is the single
        // ISecretResolver hot-path callers receive — every existing
        // SecretEnvRef row keeps working unchanged (bare name -> env).
        builder.Services.AddOptions<SecretsOptions>()
            .Bind(builder.Configuration.GetSection(SecretsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // FileSecretOptions is bound separately under [Secrets:File] so
        // FileSecretProvider can enforce the RootPath allowlist (issue #52
        // slice-1 security boundary). Without this registration the
        // provider would receive FileSecretOptions() with RootPath = null
        // and the allowlist would be silently disabled — see H1 in the
        // issue-52-slice-1 audit (2026-09-10).
        builder.Services.AddOptions<FileSecretOptions>()
            .Bind(builder.Configuration.GetSection(FileSecretOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<ISecretResolver, CompositeSecretResolver>();
        builder.Services.AddSingleton<ISecretProvider, EnvSecretProvider>();
        // The null scheme: ONE explicit registration. The file-secret
        // factory used to register a SECOND NullSecretProvider when
        // [Secrets:File]:Enabled = false (the default) — the composite's
        // duplicate-tolerant GroupBy silently masked it (issue #52
        // slice-2 audit M1/M6); the duplication is gone.
        builder.Services.AddSingleton<ISecretProvider, NullSecretProvider>();
        // File provider: registered unconditionally; FileSecretProvider
        // itself gates on [Secrets:File]:Enabled — when false it
        // short-circuits to null before any filesystem access, so a
        // file:/path ref surfaces as a typed SecretRefUnsetException
        // through the composite rather than reading the host blindly.
        // Slice 2/3 append Vault / Consul providers below the same way.
        builder.Services.AddSingleton<ISecretProvider, FileSecretProvider>();

        // Vault provider (issue #52, slice 2): [Secrets:Vault]:Enabled
        // gates the IVaultClient bootstrap. When Enabled=true the
        // factory reads the bootstrap token from the env var named in
        // VaultSecretOptions.TokenEnvRef at startup and bakes it into
        // the VaultSharp VaultClient (TokenAuthMethodInfo). When the
        // env var is unset in Production the boot fails via the
        // existing ProductionSecretValidator gate; in Development the
        // factory still throws (a placeholder token would silently
        // produce 403s on every Vault call). When Enabled=false the
        // factory uses a placeholder token — VaultSecretProvider's
        // ResolveAsync short-circuits on Enabled=false, so the client
        // is never actually used. Memory cache backs the TTL cache
        // (issue #52 §Design — remote default 60s). Slice 3 (Consul)
        // mirrors the same pattern.
        builder.Services.AddOptions<VaultSecretOptions>()
            .Bind(builder.Configuration.GetSection(VaultSecretOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<VaultSecretOptions>, VaultSecretOptionsValidator>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(Build);
        builder.Services.AddSingleton<ISecretProvider>(serviceProvider =>
            new VaultSecretProvider(
                serviceProvider.GetRequiredService<IOptions<VaultSecretOptions>>(),
                serviceProvider.GetRequiredService<IVaultClient>(),
                serviceProvider.GetRequiredService<ILogger<VaultSecretProvider>>(),
                serviceProvider.GetRequiredService<IMemoryCache>()));

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
        // the chat schema. The brain port is the gRPC client when
        // `brain:endpoint` is configured and the in-process stub when it is
        // not; the memory digest still falls back to the empty stub until
        // the memory store slice lands — TryAdd keeps the real
        // implementations winning once registered. The tool executor scopes
        // into orchestration, which Program wires above this call.
        builder.Services
            .AddChatApplication()
            .AddChatPersistence(database.ConnectionString);
        builder.Services.AddChatBrainClient(builder.Configuration);
        builder.Services.TryAddSingleton<IBrainClient, BrainStub>();
        builder.Services.TryAddSingleton<IMemoryDigest, EmptyMemoryDigest>();
        builder.Services.AddSingleton<IChatToolExecutor, HostChatToolExecutor>();
        builder.Services.AddSingleton<ChatSessionResolver>();
        builder.Services.AddScoped<IRunsReader, OrchestrationRunsReader>();
        builder.Services.AddScoped<RunsListHandler>();
        builder.Services.AddScoped<GetRunDetailHandler>();
        builder.Services.AddScoped<IApproveRunPort, HostApproveRunAdapter>();
        builder.Services.AddScoped<ICancelRunPort, HostCancelRunAdapter>();
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
        builder.Services.AddScoped<IIntakeProfileRouter, Modules.Intake.Infrastructure.Admission.IntakeProfileRouter>(static serviceProvider =>
            new Modules.Intake.Infrastructure.Admission.IntakeProfileRouter(
                serviceProvider.GetRequiredService<IOptions<IntakeWorkerDefaults>>().Value.IssueDefaultProfileKey));

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
        builder.Services.AddScoped<IWorkItemArtifactSource, OrchestrationWorkItemArtifactSource>();
        builder.Services.AddHostedService<RunArtifactPackagerHostService>();

        // Knowledge module (S10 #9): ingest + search over the pgvector
        // knowledge.memory_embeddings table. The ingestor + searcher
        // are registered through AddKnowledgeInfrastructure (which also
        // registers the IEmbeddingClient + KnowledgeIngestBackgroundService).
        // AddKnowledgePersistence wires the KnowledgeDbContext factory
        // (its own schema, its own migration history table) — the module
        // no longer reaches into Memory.Infrastructure for the context.
        builder.Services.AddKnowledgeApplication();
        builder.Services.AddKnowledgePersistence(database.ConnectionString);
        builder.Services.AddKnowledgeInfrastructure(builder.Configuration);

        // MCP server (S10 #9): JSON-RPC 2.0 over /api/v1/mcp. The
        // dispatcher is a singleton — it carries no per-call state and
        // the underlying handlers (IKnowledgeIngestor, IKnowledgeSearcher,
        // RunsListHandler) are resolved per-call by the DI container.
        builder.Services.AddSingleton<McpToolHandlers>();
        builder.Services.AddSingleton<McpServer>();

        // Scheduler module (S15): per-project cron / one-shot admission
        // source. The application façade + dispatcher worker live in
        // their own projects; the host composes the dispatcher (it knows
        // the engine — Run + WorkItem shape) and binds the two options
        // classes (poll interval + worker image / profiles-ref) from
        // configuration. Validation on start so a missing image / ref
        // fails the boot, not the first dispatch cycle.
        builder.Services
            .AddSchedulerApplication()
            .AddSchedulerPersistence(database.ConnectionString);
        builder.Services.AddScoped<ISchedulerDispatcher, SchedulerRunLauncher>();
        builder.Services.AddOptions<SchedulerOptions>()
            .Bind(builder.Configuration.GetSection(SchedulerOptions.SectionName));
        builder.Services.AddOptions<SchedulerWorkerDefaults>()
            .Bind(builder.Configuration.GetSection(SchedulerWorkerDefaults.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Projects settings back the compute scale port (live-reload store
        // replaces the in-memory default registered by AddComukiCompute).
        builder.Services.AddSingleton<Engine.Compute.Ports.IProjectScaleSettings>(
            static serviceProvider => new ProjectScaleSettingsAdapter(
                serviceProvider.GetRequiredService<Modules.Projects.Application.Ports.IProjectSettingsStore>(),
                serviceProvider.GetRequiredService<IOptions<Engine.Compute.Options.ScaleSupervisorOptions>>()));

        // The /auth/oidc/{provider}/start surface reads the configured
        // provider list for its 404s; OidcOptions is bound here so the
        // manual OIDC code-flow (OidcStartHandler / OidcCallbackHandler
        // in Identity.Application) resolves the same options instance
        // across the controller and the handlers.
        builder.Services.AddOptions<OidcOptions>()
            .Bind(builder.Configuration.GetSection(OidcOptions.SectionName));
        builder.Services.AddScoped<ICookieSigner, CookieSignerAdapter>();

        // OIDC state sweep (issue #4 tail): the start handler issues
        // 5-minute-TTL rows; the worker prunes abandoned ones on a
        // fixed interval so the table doesn't grow unbounded. Bound
        // from Host:OidcSweep, defaults match the start handler's TTL.
        builder.Services.AddOptions<OidcSweepOptions>()
            .Bind(builder.Configuration.GetSection(OidcSweepOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<OidcStateSweeper>();
        builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<OidcStateSweeper>());

        builder.Services.AddSingleton(BootstrapAdminOptions.Resolve(builder.Configuration));
        builder.Services.AddScoped<BootstrapAdminSeeder>();
        builder.Services.AddHostedService<BootstrapAdminStartupService>();

        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ProviderExceptionHandler>();

        // Identity-admin surface (issues #31-#37 + #45 read endpoints):
        // request-level validators sit alongside the module-level ones;
        // they share the same DI pipeline and the startup validator over
        // RequiresPermissionAttribute.
        builder.Services.AddScoped<IValidator<InviteUserRequest>, InviteUserRequestValidator>();
        builder.Services.AddScoped<IValidator<SetUserDisabledRequest>, SetUserDisabledRequestValidator>();
        builder.Services.AddScoped<IValidator<LinkOidcRequest>, LinkOidcRequestValidator>();
        builder.Services.AddScoped<IValidator<GrantRoleRequest>, GrantRoleRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateApiKeyRequest>, CreateApiKeyRequestValidator>();
        builder.Services.AddScoped<IValidator<ListUsersQueryRequest>, ListUsersQueryRequestValidator>();
        builder.Services.AddScoped<IValidator<ListGrantsQueryRequest>, ListGrantsQueryRequestValidator>();
        builder.Services.AddScoped<IValidator<ListApiKeysQueryRequest>, ListApiKeysQueryRequestValidator>();

        // Security pass (issue #10 T11.4): CORS allow-list for the
        // dashboard SPA + per-endpoint rate-limit partitions. Both are
        // wired before MapControllers so the named policies are visible
        // to the controller attributes; the production-secret validator
        // runs after Build() so IOptions bindings are materialised.
        builder.Services.AddComukiCors(builder.Configuration, builder.Environment);
        builder.Services.AddComukiRateLimit(builder.Configuration);

        // OpenAPI document emission (issue #29): AddOpenApi registers the
        // document in DI under the default name "v1"; MapOpenApi() at the
        // bottom of this method serves it at /openapi/v1.json for tools,
        // and the build-time source-generator emits a mirror spec to
        // artifacts/openapi.json (Comuki.Host.csproj OpenApiDocumentsDirectory).
        // The mirror is what dashboard/kubb.config.ts reads — both stay
        // consistent because both are derived from the same AddOpenApi call.
        builder.Services.AddOpenApi();

        // Realtime surface (issue #7): SignalR hub + the journal broadcast
        // interceptor. Registered after orchestration persistence — the
        // interceptor appends to the context options the engine registered
        // (Program wires persistence before Compose; the chat test fixture
        // mirrors that order). The IHostEnvironment gates
        // EnableDetailedErrors on IsDevelopment() so production containers
        // never leak stack frames into HubException messages
        // (security audit A05-1).
        builder.Services.AddComukiRealtime(builder.Environment);

        // Proxy module (issue #8 / S9 T9.6): optional OpenAI / Anthropic
        // passthrough over YARP. Virtual keys live in Proxy:* configuration
        // and authenticate via the VirtualKey scheme (Bearer vkey_xxx);
        // chat / messages routes go through MapReverseProxy and the
        // request transform rewrites the outbound auth header to the
        // upstream key the virtual key references. AddVirtualKeyAuth is
        // called inside AddProxyInfrastructure so the scheme is registered
        // on the existing authentication builder — without it
        // AddAuthentication() would wipe the Identity cookie / API-key
        // defaults.
        builder.Services.AddProxyApplication(builder.Configuration);
        builder.Services.AddProxyInfrastructure();

        // Health probes (issue #8 cross-cutting kit): Postgres SELECT 1,
        // proxy-key catalogue check. The Postgres connection string
        // is read through the same database connection the host flows
        // into every persistence layer; no second source of truth.
        builder.Services.AddSingleton<ProxyKeysHealthCheck>();
        var postgresConnectionString = database.ConnectionString;
        builder.Services.AddSingleton(_ => new PostgresHealthCheck(postgresConnectionString));

        builder.Services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(
                ComukiHealthChecks.Names.Postgres,
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"])
            .AddCheck<ProxyKeysHealthCheck>(
                ComukiHealthChecks.Names.ProxyKeys,
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);

        // Ambient subject scope: one AsyncLocal-backed accessor for the
        // whole process — the middleware installs a scope per request, the
        // worker surfaces and hosted consumers declare AsSystem, and the
        // context scope members read it inside the query filters.
        builder.Services.AddSingleton<Shared.Kernel.Scoping.ISubjectScopeAccessor, Shared.Kernel.Scoping.AsyncLocalSubjectScopeAccessor>();

        // Ambient correlation id (issue #56 §5): the console formatters
        // stamp rid=… from this slot; the middleware below installs one id
        // per request. Separate accessor from the subject scope by design.
        builder.Services.AddSingleton<Shared.Bootstrap.Correlation.ICorrelationIdAccessor, Shared.Bootstrap.Correlation.AsyncLocalCorrelationIdAccessor>();

        var app = builder.Build();

        HostDatabase.WarnLegacyAlias(database, app.Logger);

        // Production-secret gate (issue #10 T11.4): runs after the
        // service provider materialises the bound IOptions; throws in
        // Production when MinIO / bootstrap-admin still carry dev
        // defaults.
        ProductionSecretValidator.Validate(app.Services);

        // Correlation id first (issue #56 §5): outermost so even the
        // exception-handler's error logs carry rid=…; the response header
        // is set before the pipeline runs, before headers are flushed.
        app.UseMiddleware<Correlation.CorrelationIdMiddleware>();

        app.UseExceptionHandler();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseCors(CorsPolicyNames.Dashboard);
        app.UseRateLimiter();
        app.UseMiddleware<SubjectScopeMiddleware>();

        app.MapGet(ApiRoutes.Health, static () => Results.Ok(new { status = "ok" }));

        // Build info for operators and the dashboard footer (issue #56 §6):
        // version/sha/build date/mode - the same identity `comuki version`
        // prints. Anonymous by design, exactly like the health probes.
        app.MapGet(ApiRoutes.Version, static () => Results.Ok(VersionResponse.From(ComukiBuildInfo.Read())));
        app.MapHealthChecks(ApiRoutes.HealthReady, new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains("ready"),
        });
        app.MapControllers();
        app.MapProjectsEndpoints();
        app.MapCostsEndpoints();
        app.MapKnowledgeEndpoints();
        app.MapMcpEndpoints();
        app.MapComukiRealtime();
        app.MapProxyEndpoints();

        // Reverse-proxy passthrough (issue #8): chat / messages / embeddings
        // virtual-key auth runs in the VirtualKeyAuthenticationHandler the
        // AddVirtualKeyAuth call above registered; YARP forwards the request
        // body / headers and the response transform meters usage. The
        // AuthorizeAttribute scopes the auth + challenge to the VirtualKey
        // scheme so a missing bearer never falls through to the Cookie
        // redirect (RequireAuthorization's string overload is policy-named,
        // not scheme-named — the attribute sets the scheme explicitly).
        app.MapReverseProxy()
            .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute
            {
                AuthenticationSchemes = Modules.Proxy.Infrastructure.Auth.VirtualKeyAuthenticationHandler.SchemeName,
            });

        // Build-time source-generator mirrors the AddOpenApi document to
        // artifacts/openapi.json (comuki.slnx root, gitignored). This
        // MapOpenApi call serves the same document at runtime for tooling
        // (Swagger UI, Scalar, curl probes). Anonymous — the document is
        // public metadata, not an authenticated endpoint.
        app.MapOpenApi();

        return app;
    }
}
