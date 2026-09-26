using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Host;
using Comuki.Host.Cli;
using Comuki.Host.Cli.Doctor;
using Comuki.Host.OpenApi;
using Comuki.Host.Security.Tls;
using Comuki.Host.Workers;
using Comuki.Modules.Scheduler.Infrastructure.Observers;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Config;
using Comuki.Shared.Bootstrap.Logging;
using Comuki.Shared.Bootstrap.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;

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

// Comuki-native surface (issue #54): config.toml + COMUKI_* env are
// added on top of the standard JSON/env sources (double-underscore
// Helm vars like Artifacts__Endpoint keep working), [server]/
// COMUKI_SERVER_PORT pin the listen address when set, and the comuki
// console formatter owns the log output. No config.toml present →
// empty comuki layer, no error: the build-time OpenAPI pass below
// depends on booting without one.
builder.Configuration.UseComukiConfiguration();

// REST/SPA + worker-gRPC listener resolution (issue #152). The REST
// host/port keep the exact precedence [server] host/port already had
// via TryResolveServerUrl — resolved explicitly now (see
// HostTlsInstaller's remarks on why: adding the dedicated gRPC listener
// below makes Kestrel ignore UseUrls entirely, for every endpoint)
// through HostTlsInstaller's own helpers, which
// HostTlsInstaller.AddComukiTls (called below) also reuses for its
// HTTPS listener when Host:Tls is enabled.
var restListenHost = HostTlsInstaller.ResolveListenHost(builder.Configuration);
var restPort = HostTlsInstaller.ResolveHttpPort(builder.Configuration);
var workerGrpcPort = builder.Configuration.GetValue(WorkerGrpcListener.ConfigKey, WorkerGrpcListener.DefaultPort);

builder.WebHost.ConfigureKestrel(server =>
{
    server.AddServerHeader = false;
    // Public REST/SPA listener — HTTP/1.1 (+ HTTP/2 once TLS/ALPN is on;
    // harmless without it). The worker gRPC bidi stream no longer shares
    // this listener (issue #152) — it gets its own, below.
    if (restListenHost == "*")
    {
        server.ListenAnyIP(restPort, static listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
    }
    else
    {
        server.Listen(System.Net.IPAddress.Parse(restListenHost), restPort, static listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
    }

    // Dedicated HTTP/2-only listener for the worker gRPC bidi stream
    // (issue #152 root cause: Kestrel does not negotiate HTTP/2 on a
    // shared, cleartext Http1AndHttp2 endpoint without TLS — it silently
    // serves HTTP/1.1 only there, so every worker.reported journal entry
    // the stream carries was lost, confirmed by direct reproduction). A
    // protocol-pure Http2 endpoint negotiates h2c prior-knowledge
    // correctly regardless of TLS. Port pool 17000-17200 (ports.md —
    // 17185); overridable via Host:WorkerGrpcPort /
    // COMUKI_HOST_WORKERGRPCPORT.
    server.ListenAnyIP(workerGrpcPort, static listen => listen.Protocols = HttpProtocols.Http2);
});

// TLS ([Host:Tls] / COMUKI_HOST_TLS_*): adds the HTTPS listener next to
// the plain-HTTP one and registers the HTTP→HTTPS redirect. No-op while
// Enabled=false (the default) — the address resolution above stays the
// only listener source, so every existing deployment is unaffected.
builder.AddComukiTls();

builder.Logging.ClearProviders();
builder.Logging.AddComukiConsole(builder.Configuration);

// Sentry side-channel for the scheduler dispatcher (S15 / sentry):
// initialises the SDK once if Scheduler:Sentry:Dsn is set; otherwise
// stays a no-op and the SDK never enters the process. Must run before
// HostComposer.ComposeAsync wires the scheduler hosted service — the
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
// (MinIO env vars, the OIDC public-host URL, etc.) so the [Required]
// data-annotation validation in HostComposer.ComposeAsync does not fail on a
// fresh clone without an env file.
// No-op at runtime — real config comes from config.toml / env. The HMAC
// pepper seeds keep the ProductionSecretValidator fail-closed check from
// rejecting the build-time introspection pass (security audit A02-1).
// The peppers are sourced via env vars because ApiKeyOptions.Pepper and
// WorkerTokenOptions.Pepper initializers read their env vars first; the
// WorkerTokenOptions also picks up the config override via .Bind(), the
// ApiKeyOptions does not — env-var seeding covers both paths.
if (OpenApiBuildTimeExtensions.IsOpenApiDocumentGeneration)
{
    builder.Configuration["auth:publicHost:publicUrl"] = "http://build-time.invalid";
    builder.Configuration["Artifacts:Endpoint"] = "build-time:9000";
    builder.Configuration["Artifacts:AccessKey"] = "build-time";
    builder.Configuration["Artifacts:SecretKey"] = "build-time";
    builder.Configuration["Artifacts:Bucket"] = "build-time";
    builder.Configuration["Security:WorkerToken:Pepper"] = "build-time-worker-token-pepper-not-a-secret";
    Environment.SetEnvironmentVariable("COMUKI_IDENTITY_APIKEY_PEPPER", "build-time-apikey-pepper-not-a-secret");
    Environment.SetEnvironmentVariable("COMUKI_TOKEN_PEPPER", "build-time-worker-token-pepper-not-a-secret");
}

var app = await HostComposer.ComposeAsync(builder, database);

// Build banner (issue #56): the version line is the first comuki-format
// log record of the starting host.
ComukiStartupBanner.Emit(app.Services.GetRequiredService<ILoggerFactory>(), "comuki", ComukiBuildInfo.Read());

app.MapWorkerRuntime(workerGrpcPort);

await app.RunAsync();
return 0;
