using Comuki.Host.Brain;
using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.ControlPlane;
using Comuki.Host.Brain.Model;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Microsoft.Extensions.AI;
using ProtoBuf.Grpc.Server;

// The brain host: a console-shaped Kestrel app whose only surface is the
// code-first gRPC IBrainService. Composition is deliberately flat —
// memory persistence (store + sweep), the brain ports (catalog stubs) and
// the MEAI chat client over the configured OpenAI-compatible endpoint.
// The model may be unconfigured at boot (sweep + catalog still run);
// think calls fail with a setup hint until it is.
var builder = WebApplication.CreateBuilder(args);

var options = BrainOptions.Resolve(builder.Configuration);
var connectionString = BrainDatabase.Resolve(builder.Configuration);

builder.WebHost.UseUrls($"http://localhost:{options.GrpcPort}");

builder.Services.AddMemoryPersistence(connectionString);
builder.Services.AddCodeFirstGrpc();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IProfileCatalog, ControlPlaneProfileCatalog>();
builder.Services.AddSingleton<IActiveRunCatalog, StubActiveRunCatalog>();
builder.Services.AddSingleton<IExplorerReportReader, StubExplorerReportReader>();
builder.Services.AddSingleton(static serviceProvider =>
    BrainChatClientFactory.Create(serviceProvider.GetRequiredService<BrainOptions>().Model));
builder.Services.AddSingleton<BrainAgent>();
builder.Services.AddScoped<BrainGrpcService>();

var app = builder.Build();

app.MapGrpcService<BrainGrpcService>();

app.Logger.LogInformation("Comuki.Host.Brain listening on http://localhost:{GrpcPort}", options.GrpcPort);

await app.RunAsync();
