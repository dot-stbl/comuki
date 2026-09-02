using Comuki.Host.Translator;
using Comuki.Host.Translator.Api;
using Comuki.Host.Translator.Execution;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Profiles;
using Comuki.Host.Translator.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// COMUKI_* environment → Translator config section (the compute provider
// stamps these on the worker container at Start).
builder.Configuration.AddInMemoryCollection(TranslatorEnvironment.Snapshot());

builder.Services.AddOptions<TranslatorOptions>()
    .Bind(builder.Configuration.GetSection(TranslatorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPiRunner, PiRunner>();
builder.Services.AddSingleton<IProfilesProvider, ProfilesProvider>();
builder.Services.AddSingleton<HeartbeatMonitor>();
builder.Services.AddSingleton<TranslatorLoop>();
builder.Services.AddHostedService<TranslatorHostedService>();

builder.Services
    .AddOrchestratorApi()
    .AddWorkerGrpcClient();

await builder.Build().RunAsync();

/// <summary>
/// Maps the worker container's COMUKI_* environment onto the Translator
/// config section. Keeping it explicit (instead of convention binding)
/// makes the env contract visible in one place.
/// </summary>
file static class TranslatorEnvironment
{
    public static IDictionary<string, string?> Snapshot()
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Translator:OrchestratorBaseUrl"] = Environment.GetEnvironmentVariable("COMUKI_ORCH_HTTP"),
            ["Translator:OrchestratorGrpcUrl"] = Environment.GetEnvironmentVariable("COMUKI_ORCH_GRPC"),
            ["Translator:WorkerToken"] = Environment.GetEnvironmentVariable("COMUKI_WORKER_TOKEN"),
            ["Translator:ProfileKey"] = Environment.GetEnvironmentVariable("COMUKI_PROFILE_KEY"),
            ["Translator:ProfilesRef"] = Environment.GetEnvironmentVariable("COMUKI_PROFILES_REF"),
            ["Translator:WorkerImage"] = Environment.GetEnvironmentVariable("COMUKI_WORKER_IMAGE"),
            ["Translator:ProfilesPath"] = Environment.GetEnvironmentVariable("COMUKI_PROFILES_PATH"),
            ["Translator:ProfilesGitUrl"] = Environment.GetEnvironmentVariable("COMUKI_PROFILES_GIT_URL"),
            ["Translator:PiExecutable"] = Environment.GetEnvironmentVariable("COMUKI_PI_EXECUTABLE"),
            ["Translator:WorkingDirectory"] = Environment.GetEnvironmentVariable("COMUKI_WORKING_DIRECTORY"),
        };
    }
}
