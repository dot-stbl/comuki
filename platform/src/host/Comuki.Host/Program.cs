using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Host;
using Comuki.Host.Cli;
using Comuki.Host.Cli.Doctor;
using Comuki.Host.OpenApi;
using Comuki.Host.Workers;
using Comuki.Modules.Scheduler.Infrastructure.Observers;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Config;
using Comuki.Shared.Bootstrap.Logging;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;

// Operator CLI surface (issue #56): version / doctor / config show / init
// run before any host bootstrap — no config, no database and no logging
// pipeline are touched. Build-time OpenAPI generation arrives with empty
// args, so the introspection path never enters these branches.
if (ComukiHostCli.IsDoctorRequested(args))
{
    return await ComukiDoctor.RunAsync(Console.Out);
}

if (ComukiHostCli.TryRun(args) is { } cliExitCode)
{
    return cliExitCode;
}

// One resolved connection wires the whole host. HostDatabase owns the
// single read — COMUKI_DB env, then the legacy COMUKI_DATABASE alias
// (warned at startup), then ConnectionStrings:Comuki — and throws when
// absent, so the host never boots half-wired: the worker runtime
// (gRPC + claim REST) below and identity/projects inside Compose share
// the same resolved string.
//
// Under build-time OpenAPI generation (Microsoft.Extensions.ApiDescription.Server
// launches Program as the GetDocument.Insider tool) the env-var-required gate
// would throw on a plain `dotnet build` of a freshly cloned tree — substitute
// an explicit dummy connection string for the introspection pass only; the
// introspection never opens the socket and never starts the migrator.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = ComukiEnvironment.Resolve(),
});

// Comuki-native surface (issue #54): config.toml + COMUKI_* env replace
// the JSON/bare-env sources, [server]/COMUKI_SERVER_PORT pin the
// listen address when set, and the comuki console formatter owns the
// log output. No config.toml present → empty configuration, no error:
// the build-time OpenAPI pass below depends on booting without one.
builder.Configuration.UseComukiConfiguration();
builder.WebHost.ConfigureKestrel(static server => server.AddServerHeader = false);
if (builder.Configuration.TryResolveServerUrl() is { } serverUrl)
{
    builder.WebHost.UseUrls(serverUrl);
}

builder.Logging.ClearProviders();
builder.Logging.AddComukiConsole(builder.Configuration);

// Sentry side-channel for the scheduler dispatcher (S15 / sentry):
// initialises the SDK once if Scheduler:Sentry:Dsn is set; otherwise
// stays a no-op and the SDK never enters the process. Must run before
// HostComposer.Compose wires the scheduler hosted service — the
// observer's CaptureEvent relies on the SDK being initialised when DSN
// is configured.
SchedulerSentryBootstrap.TryInitialize(builder.Configuration);

var database = OpenApiBuildTimeExtensions.IsOpenApiDocumentGeneration
    ? HostDatabase.Explicit("Host=build-time-openapi;Username=openapi;Password=openapi;Database=openapi")
    : HostDatabase.Resolve(builder.Configuration);

builder.Services
    .AddOrchestrationPersistence(database.ConnectionString)
    .AddOrchestrationQueue(builder.Configuration)
    .AddOrchestrationApplication()
    .AddWorkerRuntime(builder.Configuration);

// Under build-time OpenAPI generation (GetDocument.Insider) drop our hosted
// services so the contract is emitted with zero side effects (no
// migrators/workers/seeders, no DB). No-op at runtime.
builder.Services.RemoveHostedServicesForOpenApiGeneration();

// Under build-time OpenAPI generation seed minimal config defaults
// (MinIO env vars etc.) so the [Required] data-annotation validation in
// HostComposer.Compose does not fail on a fresh clone without an env file.
// No-op at runtime — real config comes from config.toml / env. The HMAC
// pepper seeds keep the ProductionSecretValidator fail-closed check from
// rejecting the build-time introspection pass (security audit A02-1).
// The peppers are sourced via env vars because ApiKeyOptions.Pepper and
// WorkerTokenOptions.Pepper initializers read their env vars first; the
// WorkerTokenOptions also picks up the config override via .Bind(), the
// ApiKeyOptions does not — env-var seeding covers both paths.
if (OpenApiBuildTimeExtensions.IsOpenApiDocumentGeneration)
{
    builder.Configuration["Artifacts:Endpoint"] = "build-time:9000";
    builder.Configuration["Artifacts:AccessKey"] = "build-time";
    builder.Configuration["Artifacts:SecretKey"] = "build-time";
    builder.Configuration["Artifacts:Bucket"] = "build-time";
    builder.Configuration["Security:WorkerToken:Pepper"] = "build-time-worker-token-pepper-not-a-secret";
    Environment.SetEnvironmentVariable("COMUKI_IDENTITY_APIKEY_PEPPER", "build-time-apikey-pepper-not-a-secret");
    Environment.SetEnvironmentVariable("COMUKI_TOKEN_PEPPER", "build-time-worker-token-pepper-not-a-secret");
}

var app = HostComposer.Compose(builder, database);

// Build banner (issue #56): the version line is the first comuki-format
// log record of the starting host.
ComukiStartupBanner.Emit(app.Services.GetRequiredService<ILoggerFactory>(), "comuki", ComukiBuildInfo.Read());

app.MapGet(
    ApiRoutes.Profiles,
    static async (IProfileCatalog catalog, CancellationToken cancellationToken) =>
        Results.Ok(await catalog.ListAsync(cancellationToken)));
app.MapGet(
    ApiRoutes.ProfileByKey,
    static async (string key, IProfileCatalog catalog, CancellationToken cancellationToken) =>
        await catalog.GetAsync(key, cancellationToken) is { } profile
            ? Results.Ok(profile)
            : Results.NotFound());
app.MapGet(
    ApiRoutes.ChatCommands,
    static async (IChatCommandCatalog catalog, CancellationToken cancellationToken) =>
        Results.Ok(await catalog.ListCommandsAsync(cancellationToken)));

app.MapWorkerRuntime();

await app.RunAsync();
return 0;
