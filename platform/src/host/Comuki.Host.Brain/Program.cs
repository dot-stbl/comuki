using Comuki.Host.Brain;
using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.ControlPlane;
using Comuki.Host.Brain.Model;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Cli;
using Comuki.Shared.Bootstrap.Config;
using Comuki.Shared.Bootstrap.Logging;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Microsoft.Extensions.AI;
using ProtoBuf.Grpc.Server;

// Operator CLI (issue #56): `comuki-brain version` runs before any
// bootstrap and exits without touching config or the database.
if (ComukiCli.IsCommand(args, ComukiCli.VersionCommand))
{
    return ComukiCli.RunVersion("comuki-brain");
}

// The brain host: a console-shaped Kestrel app whose only surface is the
// code-first gRPC IBrainService. Composition is deliberately flat —
// memory persistence (store + sweep), the brain ports (catalog stubs) and
// the MEAI chat client over the configured OpenAI-compatible endpoint.
// The model may be unconfigured at boot (sweep + catalog still run);
// think calls fail with a setup hint until it is.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = ComukiEnvironment.Resolve(),
});

// Comuki-native surface (issue #54): config.toml + COMUKI_* env (the
// brain section and COMUKI_BRAIN_GRPCPORT land on BrainOptions through
// the env provider), no Server header, comuki console formatter.
builder.Configuration.UseComukiConfiguration();
builder.WebHost.ConfigureKestrel(static server => server.AddServerHeader = false);

builder.Logging.ClearProviders();
builder.Logging.AddComukiConsole(builder.Configuration);

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

// Build banner (issue #56): the version line is the first comuki-format
// log record of the starting host.
ComukiStartupBanner.Emit(app.Services.GetRequiredService<ILoggerFactory>(), "comuki-brain", ComukiBuildInfo.Read());

app.MapGrpcService<BrainGrpcService>();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("brain listening addr=http://localhost:{GrpcPort}", options.GrpcPort);

await app.RunAsync();
return 0;
