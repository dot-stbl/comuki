using System.Diagnostics;
using Comuki.Engine.Compute.Options;
using Comuki.Host.Security.ProductionSecrets;
using Comuki.Modules.Artifacts.Infrastructure.Store;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Config;
using Comuki.Shared.Bootstrap.Config.Toml;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Comuki.Host.Cli.Doctor;

/// <summary>
/// Pre-flight diagnostics of the <c>comuki</c> host (issue #56 §1.2):
/// an ok/warn/fail checklist over the discovery chain — config.toml
/// location, environment provenance, database connectivity, production
/// secret posture — without booting the host. Pending migrations are
/// deliberately not checked here; the line points at
/// <c>comuki-migrator status</c>. Exit code: 0 = no failures, 1 = at
/// least one fail (warnings pass).
/// </summary>
internal static class ComukiDoctor
{
    /// <summary>The exit code used when at least one check fails.</summary>
    public const int FailureExitCode = 1;

    /// <summary>Runs every check, prints the checklist and returns the exit code.</summary>
    /// <param name="writer">Output sink (stdout in production).</param>
    /// <param name="lookupEnv">Env accessor; injectable for tests, defaults to the process env.</param>
    /// <param name="probeDatabaseAsync">Connection-string probe returning the round-trip latency in milliseconds; defaults to a real Npgsql SELECT 1 with a 2-second timeout.</param>
    /// <param name="cancellationToken">Cooperative cancellation of the database probe.</param>
    public static async Task<int> RunAsync(
        TextWriter writer,
        Func<string, string?>? lookupEnv = null,
        Func<string, CancellationToken, Task<int>>? probeDatabaseAsync = null,
        CancellationToken cancellationToken = default)
    {
        var env = lookupEnv ?? Environment.GetEnvironmentVariable;
        var configuration = new ConfigurationBuilder()
            .UseComukiConfiguration()
            .Build();

        var checks = new List<DoctorCheck>
        {
            ComukiDoctorChecks.CheckConfigFile(),
            ComukiDoctorChecks.CheckEnvironment(env),
            await ComukiDoctorChecks.CheckDatabaseAsync(configuration, probeDatabaseAsync, cancellationToken)
        };
        checks.AddRange(ComukiDoctorChecks.CheckSecrets(env));

        foreach (var check in ComukiDoctorChecks.WithMigrationsHint(checks))
        {
            writer.WriteLine(check.Render());
        }

        var failed = checks.Count(static check => check.Status == DoctorCheckStatus.Fail);
        var warned = checks.Count(static check => check.Status == DoctorCheckStatus.Warn);
        writer.WriteLine($"# {checks.Count - failed - warned} ok, {warned} warn, {failed} fail");

        return failed == 0 ? 0 : FailureExitCode;
    }
}

/// <summary>The doctor's individual checks.</summary>
file static class ComukiDoctorChecks
{
    public static DoctorCheck CheckConfigFile()
    {
        return ComukiConfigFile.Find() is { } path
            ? new DoctorCheck("config", DoctorCheckStatus.Ok, $"path={path}")
            : new DoctorCheck(
                "config",
                DoctorCheckStatus.Warn,
                "no config.toml found (chain: COMUKI_CONFIG_PATH → ./config.toml → /etc/comuki/config.toml); running on env-only configuration");
    }

    public static DoctorCheck CheckEnvironment(Func<string, string?> lookupEnv)
    {
        var resolution = ComukiEnvironment.ResolveDetailed(lookupEnv);
        var source = resolution.Variable ?? "default";
        return resolution.FromPrimary
            ? new DoctorCheck("environment", DoctorCheckStatus.Ok, $"env={resolution.Environment} source={source}")
            : new DoctorCheck(
                "environment",
                DoctorCheckStatus.Warn,
                $"env={resolution.Environment} source={source} (deprecated fallback — prefer {ComukiEnvironment.EnvironmentVariable})");
    }

    public static async Task<DoctorCheck> CheckDatabaseAsync(
        IConfiguration configuration,
        Func<string, CancellationToken, Task<int>>? probeDatabaseAsync,
        CancellationToken cancellationToken)
    {
        if (HostDatabase.TryResolve(configuration) is not { } connection)
        {
            return new DoctorCheck(
                "database",
                DoctorCheckStatus.Fail,
                "connection string not found: set the COMUKI_DB env var or connectionStrings.comuki in config.toml");
        }

        if (connection.FromLegacyAlias)
        {
            return new DoctorCheck(
                "database",
                DoctorCheckStatus.Warn,
                $"connection resolved from the legacy {HostDatabase.LegacyEnvVariable} env var; rename it to {HostDatabase.EnvVariable}");
        }

        var probe = probeDatabaseAsync ?? ComukiDoctorDatabase.ProbeAsync;
        try
        {
            var latencyMs = await probe(connection.ConnectionString, cancellationToken);
            return new DoctorCheck("database", DoctorCheckStatus.Ok, $"connected latency={latencyMs}ms");
        }
        catch (Exception exception)
        {
            return new DoctorCheck("database", DoctorCheckStatus.Fail, $"unreachable error={exception.Message}");
        }
    }

    public static IReadOnlyList<DoctorCheck> CheckSecrets(Func<string, string?> lookupEnv)
    {
        var environment = ComukiEnvironment.ResolveDetailed(lookupEnv);
        var configuration = new ConfigurationBuilder()
            .UseComukiConfiguration()
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new ComukiDoctorHostEnvironment(environment.Environment))
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(Options.Create(ComukiDoctorSecrets.BindArtifacts(configuration)))
            .AddSingleton(Options.Create(configuration.GetSection(ApiKeyOptions.SectionName).Get<ApiKeyOptions>() ?? new ApiKeyOptions()))
            .AddSingleton(Options.Create(configuration.GetSection(WorkerTokenOptions.SectionName).Get<WorkerTokenOptions>() ?? new WorkerTokenOptions()))
            .AddSingleton(Options.Create(configuration.GetSection(SecretsOptions.SectionName).Get<SecretsOptions>() ?? new SecretsOptions()))
            .AddSingleton(Options.Create(configuration.GetSection(VaultSecretOptions.SectionName).Get<VaultSecretOptions>() ?? new VaultSecretOptions()))
            .BuildServiceProvider();

        return [.. ProductionSecretAudit.Collect(services)
            .Select(static finding => new DoctorCheck(
                $"secrets:{finding.Name}",
                finding.SeverityLevel switch
                {
                    ProductionSecretFinding.Severity.Ok => DoctorCheckStatus.Ok,
                    ProductionSecretFinding.Severity.Warn => DoctorCheckStatus.Warn,
                    _ => DoctorCheckStatus.Fail,
                },
                finding.Detail))];
    }

    public static IReadOnlyList<DoctorCheck> WithMigrationsHint(List<DoctorCheck> checks)
    {
        checks.Add(new DoctorCheck("migrations", DoctorCheckStatus.Ok, "not checked here — run `comuki-migrator status`"));
        return checks;
    }
}

/// <summary>Npgsql SELECT 1 round-trip with a 2-second timeout; returns the latency in milliseconds.</summary>
file static class ComukiDoctorDatabase
{
    public static async Task<int> ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = 2;
        await command.ExecuteScalarAsync(cancellationToken);
        return (int)stopwatch.ElapsedMilliseconds;
    }
}

/// <summary>Options plumbing of the secrets check: an absent [artifacts] section is audited as its dev defaults.</summary>
file static class ComukiDoctorSecrets
{
    public static ArtifactsOptions BindArtifacts(IConfiguration configuration)
    {
        // A missing [artifacts] section means the deployment relies on the
        // committed dev defaults — audit it as exactly that (fail in
        // Production, warn outside) instead of pretending it is configured.
        return configuration.GetSection(ArtifactsOptions.SectionName).Get<ArtifactsOptions>()
            ?? new ArtifactsOptions
            {
                Endpoint = "unset",
                AccessKey = "comuki",
                SecretKey = "comuki_dev",
                Bucket = "unset",
            };
    }
}

/// <summary>Minimal IHostEnvironment over the resolved COMUKI_ENV value.</summary>
file sealed class ComukiDoctorHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "comuki-doctor";

    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
