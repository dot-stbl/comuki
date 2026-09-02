using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Host;
using Comuki.Host.Workers;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;

// One resolved connection wires the whole host. HostDatabase owns the
// single read — COMUKI_DB env, then the legacy COMUKI_DATABASE alias
// (warned at startup), then ConnectionStrings:Comuki — and throws when
// absent, so the host never boots half-wired: the worker runtime
// (gRPC + claim REST) below and identity/projects inside Compose share
// the same resolved string.
var builder = WebApplication.CreateBuilder(args);

var database = HostDatabase.Resolve(builder.Configuration);

builder.Services
    .AddOrchestrationPersistence(database.ConnectionString)
    .AddOrchestrationQueue(builder.Configuration)
    .AddOrchestrationApplication()
    .AddWorkerRuntime(builder.Configuration);

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
