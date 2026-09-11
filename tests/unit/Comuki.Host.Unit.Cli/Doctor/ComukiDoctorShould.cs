using System.Text;
using Comuki.Host.Cli.Doctor;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Cli.Doctor;

/// <summary>
/// <c>comuki doctor</c> (issue #56 §1.2): config discovery, environment
/// provenance, database probe and secret posture rendered as an ok/warn/fail
/// checklist; exit 1 only when a check fails. Env-sensitive — the whole
/// class rides the serialized collection.
/// </summary>
[Collection(nameof(CliEnvSafeCollection))]
public sealed class ComukiDoctorShould
{
    [Fact(DisplayName = "Given a config file, a healthy probe and overridden secrets, when doctor runs, then every line is ok and exit is 0")]
    public async Task AllChecksPassWithOverriddenSecretsAsync()
    {
        using var toml = TempTomlScope.Install("""
            [server]
            port = 18080

            [artifacts]
            endpoint = "minio:9000"
            bucket = "comuki-run-bundles"
            accessKey = "service-account"
            secretKey = "rotated-strong-secret"
            """);
        using var env = EnvVarScope.Set(
            new EnvVarEntry("COMUKI_DB", "Host=probe"),
            new EnvVarEntry("COMUKI_IDENTITY_APIKEY_PEPPER", "rotated-strong-apikey-pepper-2026"),
            new EnvVarEntry("COMUKI_TOKEN_PEPPER", "rotated-strong-worker-token-pepper-2026"));
        var writer = new LineWriter();

        var exitCode = await ComukiDoctor.RunAsync(
            writer,
            lookupEnv: static name => name == "COMUKI_ENV" ? "development" : null,
            probeDatabaseAsync: static (_, _) => Task.FromResult(3),
            cancellationToken: TestContext.Current.CancellationToken);

        var lines = writer.Lines;
        exitCode.ShouldBe(0);
        lines.ShouldContain(line => line.StartsWith("ok    config", StringComparison.Ordinal) && line.Contains($"path={toml.PathOf()}"));
        lines.ShouldContain(static line => line.StartsWith("ok    environment", StringComparison.Ordinal) && line.Contains("env=development source=COMUKI_ENV"));
        lines.ShouldContain(static line => line.StartsWith("ok    database", StringComparison.Ordinal) && line.Contains("connected latency=3ms"));
        lines.ShouldContain(static line => line.StartsWith("ok    secrets:apikey-pepper", StringComparison.Ordinal) && line.Contains("COMUKI_IDENTITY_APIKEY_PEPPER is set"));
        lines.ShouldContain(static line => line.StartsWith("ok    secrets:worker-token-pepper", StringComparison.Ordinal) && line.Contains("COMUKI_TOKEN_PEPPER is set"));
        lines.ShouldContain(static line => line.StartsWith("ok    migrations", StringComparison.Ordinal) && line.Contains("comuki-migrator status"));
        lines.ShouldContain("# 10 ok, 0 warn, 0 fail");
    }

    [Fact(DisplayName = "Given an unreachable database, when doctor runs, then the database line fails and exit is 1")]
    public async Task FailOnUnreachableDatabaseAsync()
    {
        using var env = EnvVarScope.Set(new EnvVarEntry("COMUKI_DB", "Host=probe"));
        var writer = new LineWriter();

        var exitCode = await ComukiDoctor.RunAsync(
            writer,
            lookupEnv: static name => name == "COMUKI_ENV" ? "development" : null,
            probeDatabaseAsync: static (_, _) => Task.FromException<int>(new InvalidOperationException("connection refused")),
            cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(1);
        writer.Lines.ShouldContain(static line => line.StartsWith("fail  database", StringComparison.Ordinal) && line.Contains("unreachable error=connection refused"));
    }

    [Fact(DisplayName = "Given no connection string anywhere, when doctor runs, then the database line fails with the setup hint")]
    public async Task FailWhenConnectionStringMissingAsync()
    {
        using var toml = TempTomlScope.Install("[server]\nport = 18080\n");
        using var env = EnvVarScope.Set(
            new EnvVarEntry("COMUKI_DB", null),
            new EnvVarEntry("COMUKI_DATABASE", null));
        var writer = new LineWriter();

        var exitCode = await ComukiDoctor.RunAsync(writer, lookupEnv: static _ => null, cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(1);
        writer.Lines.ShouldContain(static line => line.StartsWith("fail  database", StringComparison.Ordinal) && line.Contains("connection string not found"));
    }

    [Fact(DisplayName = "Given the legacy COMUKI_DATABASE alias, when doctor runs, then the database line warns about the rename")]
    public async Task WarnOnLegacyDatabaseAliasAsync()
    {
        using var env = EnvVarScope.Set(
            new EnvVarEntry("COMUKI_DB", null),
            new EnvVarEntry("COMUKI_DATABASE", "Host=probe"),
            new EnvVarEntry("COMUKI_IDENTITY_APIKEY_PEPPER", "rotated-strong-apikey-pepper-2026"),
            new EnvVarEntry("COMUKI_TOKEN_PEPPER", "rotated-strong-worker-token-pepper-2026"));
        var writer = new LineWriter();

        var exitCode = await ComukiDoctor.RunAsync(
            writer,
            lookupEnv: static name => name == "COMUKI_ENV" ? "development" : null,
            cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(0);
        writer.Lines.ShouldContain(static line => line.StartsWith("warn  database", StringComparison.Ordinal) && line.Contains("legacy COMUKI_DATABASE"));
    }

    [Fact(DisplayName = "Given no config.toml on the chain, when doctor runs, then the config line warns")]
    public async Task WarnWhenConfigMissingAsync()
    {
        using var env = EnvVarScope.Set(new EnvVarEntry("COMUKI_CONFIG_PATH", null));
        var writer = new LineWriter();

        await ComukiDoctor.RunAsync(writer, lookupEnv: static _ => null, cancellationToken: TestContext.Current.CancellationToken);

        writer.Lines.ShouldContain(static line => line.StartsWith("warn  config", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given only the ASPNETCORE_ENVIRONMENT fallback, when doctor runs, then the environment line warns about the deprecated fallback")]
    public async Task WarnOnDeprecatedEnvironmentFallbackAsync()
    {
        var writer = new LineWriter();

        await ComukiDoctor.RunAsync(
            writer,
            lookupEnv: static name => name == "ASPNETCORE_ENVIRONMENT" ? "Development" : null,
            cancellationToken: TestContext.Current.CancellationToken);

        writer.Lines.ShouldContain(static line => line.StartsWith("warn  environment", StringComparison.Ordinal) && line.Contains("deprecated fallback"));
    }

    [Fact(DisplayName = "Given dev-default peppers in Development, when doctor runs, then the secret lines warn and exit stays 0")]
    public async Task WarnOnDevDefaultSecretsOutsideProductionAsync()
    {
        using var env = EnvVarScope.Set(
            new EnvVarEntry("COMUKI_DB", "Host=probe"),
            new EnvVarEntry("COMUKI_IDENTITY_APIKEY_PEPPER", null),
            new EnvVarEntry("COMUKI_TOKEN_PEPPER", null));
        var writer = new LineWriter();

        var exitCode = await ComukiDoctor.RunAsync(
            writer,
            lookupEnv: static name => name == "COMUKI_ENV" ? "development" : null,
            probeDatabaseAsync: static (_, _) => Task.FromResult(3),
            cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(0);
        writer.Lines.ShouldContain(static line => line.StartsWith("warn  secrets:apikey-pepper", StringComparison.Ordinal));
        writer.Lines.ShouldContain(static line => line.StartsWith("warn  secrets:worker-token-pepper", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given dev-default peppers in Production, when doctor runs, then the secret lines fail and exit is 1")]
    public async Task FailOnDevDefaultSecretsInProductionAsync()
    {
        using var env = EnvVarScope.Set(
            new EnvVarEntry("COMUKI_DB", "Host=probe"),
            new EnvVarEntry("COMUKI_IDENTITY_APIKEY_PEPPER", null),
            new EnvVarEntry("COMUKI_TOKEN_PEPPER", null));
        var writer = new LineWriter();

        var exitCode = await ComukiDoctor.RunAsync(
            writer,
            lookupEnv: static name => name == "COMUKI_ENV" ? "production" : null,
            probeDatabaseAsync: static (_, _) => Task.FromResult(3),
            cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(1);
        writer.Lines.ShouldContain(static line => line.StartsWith("fail  secrets:apikey-pepper", StringComparison.Ordinal));
    }

    /// <summary>Line-capturing TextWriter.</summary>
    private sealed class LineWriter : TextWriter
    {
        private readonly StringBuilder builder = new();

        public IReadOnlyList<string> Lines => builder.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        public override Encoding Encoding { get; } = Encoding.UTF8;

        public override void Write(string? value)
        {
            builder.Append(value);
        }

        public override void WriteLine(string? value)
        {
            builder.Append(value).Append('\n');
        }
    }
}
