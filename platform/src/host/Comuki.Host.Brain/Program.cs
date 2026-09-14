using Comuki.Host.Brain;
using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.ControlPlane;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Cli;
using Comuki.Shared.Bootstrap.Config;
using Comuki.Shared.Bootstrap.Logging;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// Operator CLI (issue #56): `comuki-brain version` runs before any
// bootstrap and exits without touching config or the database.
if (ComukiCli.IsCommand(args, ComukiCli.VersionCommand))
{
    return ComukiCli.RunVersion("comuki-brain");
}

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
builder.Logging.AddComukiConsole(builder.Configuration);

var options = BrainOptions.Resolve(builder.Configuration);
var connectionString = BrainDatabase.Resolve(builder.Configuration);

// gRPC needs h2c (unencrypted HTTP/2 with prior knowledge) — Kestrel
// endpoint defaults are HTTP/1.1, which rejects the HTTP/2 preface.
builder.WebHost.ConfigureKestrel(server =>
{
    server.AddServerHeader = false;
    server.ListenAnyIP(options.GrpcPort, static listen => listen.Protocols = HttpProtocols.Http2);
});

// The brain owns no subject of its own — it is the platform's own
// orchestrating intelligence, not a request handled on a human's behalf
// (BrainRequest carries no caller identity), so BrainAgent declares
// itself an explicit system consumer via AsSystem before it ever touches
// memory. Declared explicitly here (AddMemoryPersistence below also
// TryAdds one as a resolvability fallback for hosts that never think
// about scope at all) so that declaration is enforced, not assumed: an
// established accessor throws loudly if a future flow reads memory
// without declaring one, instead of silently defaulting open.
builder.Services.TryAddSingleton<ISubjectScopeAccessor, AsyncLocalSubjectScopeAccessor>();
builder.Services.AddMemoryPersistence(connectionString);
builder.Services.AddCodeFirstGrpc();

// Secret resolution (issue #53, slice 2/3 follow-up): the brain host
// wires the same per-provider DI graph the main host uses
// (CompositeSecretResolver + Env / Null / File / Vault) so a
// `vault:models/brain#endpoint` style *Ref on BrainOptions resolves
// through the per-call IModelConfigProvider (issue #53). The Vault
// provider self-gates on VaultSecretOptions.Enabled (default false),
// so an unconfigured Brain deployment (developer setups,
// single-container installs) keeps its existing "first think call
// fails with a setup hint" behaviour — a `vault:` ref now surfaces
// as SecretRefUnsetException through the composite rather than
// SecretRefFormatException from a missing-provider path. Consul
// provider is tracked as a follow-up (ConsulSecretProvider does not
// exist in Comuki.Shared.Kernel yet).
builder.Services.AddBrainSecrets(builder.Configuration);

// Model config (issue #53): per-call resolution through the secret
// resolver above. The agent loop builds a fresh IChatClient from the
// resolved config on every invocation so a Vault / Consul rotation
// lands within the resolver's TTL (60s default) without a host
// restart.
builder.Services.AddSingleton<IModelConfigProvider, ModelConfigProvider>();
builder.Services.AddSingleton<IBrainChatClientFactory, DefaultBrainChatClientFactory>();

// Options (issue #53 audit): the AddOptions pipeline binds [brain] and
// enforces the [Range] caps at startup; the pre-build instance resolved
// above (the same values that configured the Kestrel URL) is what
// IOptions<BrainOptions> consumers (BrainAgent, ModelConfigProvider)
// receive — the old bare AddSingleton(options) left them with an
// unbound default, silently skipping the secret-ref resolution.
builder.Services.AddBrainOptions(builder.Configuration, options);
// Bare BrainOptions for direct-inject consumers (ControlPlaneProfileCatalog)
// — same pinned instance, not a second resolution.
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IProfileCatalog, ControlPlaneProfileCatalog>();
builder.Services.AddSingleton<IActiveRunCatalog, StubActiveRunCatalog>();
builder.Services.AddSingleton<IExplorerReportReader, StubExplorerReportReader>();
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
