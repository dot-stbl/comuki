using System.Security.Cryptography;

namespace Comuki.Host.Cli.Init;

/// <summary>
/// Flag payload of <c>comuki init</c> (issue #56 §2): the target
/// environment and the overwrite switch. Kept as a record with a parse
/// core so the dispatcher never touches string flags directly.
/// </summary>
/// <param name="Environment">Value written as COMUKI_ENV (default <c>development</c>).</param>
/// <param name="Force">True when existing files may be overwritten.</param>
internal sealed record ComukiInitOptions(string Environment, bool Force)
{
    /// <summary>Environment used when <c>--env</c> is not supplied.</summary>
    public const string DefaultEnvironment = "development";

    /// <summary>Parses <c>init [--env &lt;name&gt;] [--force]</c>; null on an unknown flag or a missing value.</summary>
    public static ComukiInitOptions? Parse(string[] args)
    {
        var environment = DefaultEnvironment;
        var force = false;

        for (var index = 1; index < args.Length; index++)
        {
            if (string.Equals(args[index], ComukiInit.ForceFlag, StringComparison.Ordinal))
            {
                force = true;
                continue;
            }

            if (string.Equals(args[index], ComukiInit.EnvFlag, StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    return null;
                }

                environment = args[index + 1].Trim();
                index++;
                continue;
            }

            return null;
        }

        return new ComukiInitOptions(environment, force);
    }
}

/// <summary>
/// First-run generator (issue #56 §2): <c>comuki init [--env &lt;name&gt;]
/// [--force]</c> writes a commented <c>./config.toml</c> from the embedded
/// template and a <c>./.env</c> skeleton (COMUKI_ENV, COMUKI_DB
/// placeholder, fresh random-hex HMAC peppers). Non-interactive by design
/// — flags only. Existing files are never overwritten without
/// <c>--force</c>; any skipped file fails the run (exit 1), a usage error
/// exits 2.
/// </summary>
internal static class ComukiInit
{
    /// <summary>Flag enabling overwrite of existing files.</summary>
    public const string ForceFlag = "--force";

    /// <summary>Flag selecting the environment written as COMUKI_ENV.</summary>
    public const string EnvFlag = "--env";

    /// <summary>The config.toml file created in the working directory.</summary>
    public const string ConfigFileName = "config.toml";

    /// <summary>The .env skeleton created in the working directory.</summary>
    public const string EnvFileName = ".env";

    /// <summary>Runs the generator; returns the process exit code (0 written, 1 existing files skipped, 2 usage error).</summary>
    /// <param name="args">Full argv (the first element is the <c>init</c> command itself).</param>
    /// <param name="writer">Progress sink (stdout in production).</param>
    /// <param name="workingDirectory">Target directory; defaults to the process working directory.</param>
    /// <param name="generatePepper">Pepper generator for tests; defaults to 32 random bytes as hex.</param>
    public static int Run(string[] args, TextWriter writer, string? workingDirectory = null, Func<string>? generatePepper = null)
    {
        if (ComukiInitOptions.Parse(args) is not { } options)
        {
            writer.WriteLine($"usage: comuki init [{EnvFlag} <development|production>] [{ForceFlag}]");
            return 2;
        }

        var directory = workingDirectory ?? Directory.GetCurrentDirectory();
        var pepper = generatePepper ?? GeneratePepper;
        var exitCode = 0;

        exitCode = WriteTarget(writer, directory, ConfigFileName, ComukiInitTemplates.ConfigToml, options.Force, exitCode);
        exitCode = WriteTarget(
            writer,
            directory,
            EnvFileName,
            ComukiInitTemplates.EnvFile(options.Environment, pepper(), pepper()),
            options.Force,
            exitCode);

        return exitCode;
    }

    /// <summary>One pepper value: 32 random bytes as 64 hex characters.</summary>
    public static string GeneratePepper()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    /// <summary>Writes one target file unless it exists without --force; folds the outcome into the running exit code.</summary>
    internal static int WriteTarget(TextWriter writer, string directory, string fileName, string content, bool force, int exitCode)
    {
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path) && !force)
        {
            writer.WriteLine($"skipped file={fileName} reason=exists (use {ForceFlag} to overwrite)");
            return 1;
        }

        File.WriteAllText(path, content);
        writer.WriteLine($"created file={fileName}");
        return exitCode;
    }
}

/// <summary>Embedded templates of <c>comuki init</c>: the config.toml base and the .env skeleton.</summary>
file static class ComukiInitTemplates
{
    public static string ConfigToml =>
        """
        # comuki config.toml — generated by `comuki init`.
        #
        # Values here can be overridden by env: COMUKI_A_B → a:b (single
        # underscore = section separator, binding case-insensitive).
        # Example: COMUKI_SERVER_PORT=18080 overrides [server] port below.
        #
        # NEVER put secrets in this file — credentials go through env vars
        # (see the generated .env): COMUKI_DB, COMUKI_IDENTITY_APIKEY_PEPPER,
        # COMUKI_TOKEN_PEPPER, COMUKI_BOOTSTRAP_ADMIN_PASSWORD,
        # COMUKI_ARTIFACTS_ACCESSKEY / _SECRETKEY, COMUKI_BRAIN_MODEL_API_KEY.

        [server]
        # HTTP listener of the orchestrator host. Commented out = the default
        # mechanism (in containers: ASPNETCORE_HTTP_PORTS from the base image).
        # host = "0.0.0.0"
        # port = 8080

        [brain]
        # Standalone brain host (comuki-brain): gRPC listener port from the
        # Comuki port pool (17000–17200; 17004 is the brain's row).
        grpcPort = 17004
        # maxToolIterations = 8

        [brain.model]
        # Any OpenAI-compatible endpoint. The API key stays in env:
        # COMUKI_BRAIN_MODEL_API_KEY.
        # endpoint = "https://api.example.com/v1"
        # modelId = "glm-4.7"

        [host.cors]
        # Browser origins allowed on the API.
        # allowedOrigins = ["http://localhost:17173"]

        [artifacts]
        # S3-compatible store for run bundles (MinIO). Keys via env:
        # COMUKI_ARTIFACTS_ACCESSKEY / COMUKI_ARTIFACTS_SECRETKEY.
        # endpoint = "minio:9000"
        # bucket = "comuki-run-bundles"
        # useSSL = false
        # autoCreateBucket = true

        [compute]
        # "docker" (default) or "kubernetes" — how worker containers spawn.
        # provider = "docker"

        [telemetry]
        # OTLP gRPC endpoint (VictoriaMetrics :8431 in the compose stack);
        # unset = no OTel SDK.
        # otlpEndpoint = "http://victoriametrics:8431"

        [connectionStrings]
        # Fallback for the migrator when COMUKI_DB / COMUKI_DATABASE are unset.
        # Blank Password= is filled from COMUKI_MIGRATOR_DB_PASSWORD.
        # comuki = "Host=localhost;Port=5432;Database=comuki;Username=comuki;Password="
        """;

    public static string EnvFile(string environment, string apiKeyPepper, string workerTokenPepper)
    {
        return $"""
        # comuki environment — generated by `comuki init`.
        # Secrets live in env only, never in config.toml.

        # development | production (COMUKI_ENV; ASPNETCORE_/DOTNET_ fallbacks still work)
        COMUKI_ENV={environment}

        # Postgres connection used by the host and the migrator.
        COMUKI_DB=Host=localhost;Port=5432;Database=comuki;Username=comuki;Password=change-me

        # HMAC peppers (random hex, generated). Rotating invalidates the stored
        # key/token hashes by design.
        COMUKI_IDENTITY_APIKEY_PEPPER={apiKeyPepper}
        COMUKI_TOKEN_PEPPER={workerTokenPepper}

        # Bootstrap admin — set before the first production boot; the startup
        # gate rejects dev defaults and weak passwords.
        # COMUKI_BOOTSTRAP_ADMIN_EMAIL=ops@example.com
        # COMUKI_BOOTSTRAP_ADMIN_PASSWORD=change-me-strong-1!
        """;
    }
}
