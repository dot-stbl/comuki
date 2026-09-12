using Comuki.Host.Brain;
using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.ControlPlane;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Config;
using Comuki.Shared.Bootstrap.Logging;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Comuki.Shared.Kernel.Secrets;
using ProtoBuf.Grpc.Server;

// The brain host: a console-shaped Kestrel app whose only surface is the
// code-first gRPC IBrainService. Composition is deliberately flat —
// memory persistence (store + sweep), the brain ports (catalog stubs)
// and the MEAI chat client built per invocation from
// IModelConfigProvider (issue #53). The model may be unconfigured at
// boot (sweep + catalog still run); think calls fail with a setup
// hint until it is.
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
builder.Logging.AddComukiConsole();

var options = BrainOptions.Resolve(builder.Configuration);
var connectionString = BrainDatabase.Resolve(builder.Configuration);

builder.WebHost.UseUrls($"http://localhost:{options.GrpcPort}");

builder.Services.AddMemoryPersistence(connectionString);
builder.Services.AddCodeFirstGrpc();

// Secret resolution (issue #53): the brain host ships the minimal
// ISecretResolver wiring so the per-call IModelConfigProvider can
// resolve refs from the host's local secrets. The Brain is a
// separate process; the Vault / Consul provider wiring lives in
// Comuki.Host (HostComposer.cs) because it requires the
// production-secret validation gate and OIDC-style env handling
// shared with the rest of the platform. For Brain-only deployments
// (developer setups, single-container installs) the Env provider
// alone covers `env:GH_TOKEN`-style refs; Vault / Consul support
// in Brain is tracked as a follow-up. The host still boots without
// any provider answering the configured refs — the env / null
// providers always register, the resolver's short-circuit to null
// for a missing ref preserves the "first think call fails with a
// setup hint" behaviour that pre-existed.
builder.Services.AddSingleton<ISecretResolver, CompositeSecretResolver>();
builder.Services.AddSingleton<ISecretProvider, EnvSecretProvider>();
builder.Services.AddSingleton<ISecretProvider, NullSecretProvider>();

// Model config (issue #53): per-call resolution through the secret
// resolver above. The agent loop builds a fresh IChatClient from the
// resolved config on every invocation so a Vault / Consul rotation
// lands within the resolver's TTL (60s default) without a host
// restart.
builder.Services.AddSingleton<IModelConfigProvider, ModelConfigProvider>();
builder.Services.AddSingleton<IBrainChatClientFactory, DefaultBrainChatClientFactory>();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IProfileCatalog, ControlPlaneProfileCatalog>();
builder.Services.AddSingleton<IActiveRunCatalog, StubActiveRunCatalog>();
builder.Services.AddSingleton<IExplorerReportReader, StubExplorerReportReader>();
builder.Services.AddSingleton<BrainAgent>();
builder.Services.AddScoped<BrainGrpcService>();

var app = builder.Build();

app.MapGrpcService<BrainGrpcService>();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("brain listening addr=http://localhost:{GrpcPort}", options.GrpcPort);

await app.RunAsync();
return 0;
