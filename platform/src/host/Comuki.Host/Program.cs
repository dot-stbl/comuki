using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Host;
using Comuki.Host.Workers;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;

// Worker runtime (gRPC + claim REST) comes up only when a database is wired;
// without one the host stays the catalog/identity surface. Compose below is
// the single composition point for everything else.
var builder = WebApplication.CreateBuilder(args);

var database = builder.Configuration["COMUKI_DATABASE"];
if (!string.IsNullOrWhiteSpace(database))
{
    _ = builder.Services
        .AddOrchestrationPersistence(database)
        .AddOrchestrationQueue(builder.Configuration)
        .AddOrchestrationApplication()
        .AddWorkerRuntime(builder.Configuration);
}

var app = HostComposer.Compose(builder);

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

if (!string.IsNullOrWhiteSpace(database))
{
    app.MapWorkerRuntime();
}

await app.RunAsync();
