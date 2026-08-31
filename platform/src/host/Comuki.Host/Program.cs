using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Host;
using Comuki.Host.ControlPlane;
using Comuki.Host.Workers;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControlPlaneCatalogCore(builder.Configuration);

// Worker runtime (gRPC + claim REST) comes up only when a database is wired;
// without one the host stays the thin catalog/health surface.
var database = builder.Configuration["COMUKI_DATABASE"];
if (!string.IsNullOrWhiteSpace(database))
{
    _ = builder.Services
        .AddOrchestrationPersistence(database)
        .AddOrchestrationQueue(builder.Configuration)
        .AddOrchestrationApplication()
        .AddWorkerRuntime(builder.Configuration);
}

var app = builder.Build();

app.MapGet(ApiRoutes.Health, static () => Results.Ok(new { status = "ok" }));
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
