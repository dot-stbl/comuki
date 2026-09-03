using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Host;
using Comuki.Host.OpenApi;
using Comuki.Host.Workers;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;

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
var builder = WebApplication.CreateBuilder(args);

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
// No-op at runtime — real config comes from appsettings / env.
if (OpenApiBuildTimeExtensions.IsOpenApiDocumentGeneration)
{
    builder.Configuration["Artifacts:Endpoint"] = "build-time:9000";
    builder.Configuration["Artifacts:AccessKey"] = "build-time";
    builder.Configuration["Artifacts:SecretKey"] = "build-time";
    builder.Configuration["Artifacts:Bucket"] = "build-time";
}

var app = HostComposer.Compose(builder, database);

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
