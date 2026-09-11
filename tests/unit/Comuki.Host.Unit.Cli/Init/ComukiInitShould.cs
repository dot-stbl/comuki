using System.Text;
using Comuki.Host.Cli.Init;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Cli.Init;

/// <summary>
/// First-run generator (issue #56 §2): writes config.toml + .env into the
/// working directory, generates fresh hex peppers, refuses to overwrite
/// without --force, and rejects unknown flags with a usage hint.
/// </summary>
public sealed class ComukiInitShould : IDisposable
{
    private readonly string workingDirectory = Directory.CreateTempSubdirectory("comuki-init-test").FullName;

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(workingDirectory, recursive: true);
    }

    [Fact(DisplayName = "Given an empty directory, when init runs, then config.toml and .env are written and exit is 0")]
    public void WriteConfigAndEnvOnFirstRun()
    {
        var writer = new LineWriter();

        var exitCode = ComukiInit.Run(["init"], writer, workingDirectory, static () => "cafe01");

        exitCode.ShouldBe(0);
        writer.Lines.ShouldContain($"created file={ComukiInit.ConfigFileName}");
        writer.Lines.ShouldContain($"created file={ComukiInit.EnvFileName}");
        File.Exists(Path.Combine(workingDirectory, ComukiInit.ConfigFileName)).ShouldBeTrue();
        File.Exists(Path.Combine(workingDirectory, ComukiInit.EnvFileName)).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given init with an env flag, when it runs, then COMUKI_ENV carries the flag value")]
    public void WriteRequestedEnvironment()
    {
        var writer = new LineWriter();

        ComukiInit.Run(["init", "--env", "production"], writer, workingDirectory, static () => "cafe01");

        EnvFileContent().ShouldContain("COMUKI_ENV=production");
    }

    [Fact(DisplayName = "Given init without an env flag, when it runs, then COMUKI_ENV defaults to development")]
    public void DefaultEnvironmentIsDevelopment()
    {
        var writer = new LineWriter();

        ComukiInit.Run(["init"], writer, workingDirectory, static () => "cafe01");

        EnvFileContent().ShouldContain("COMUKI_ENV=development");
    }

    [Fact(DisplayName = "Given generated peppers, when the .env is written, then both pepper variables carry distinct hex values")]
    public void WriteDistinctHexPeppers()
    {
        var writer = new LineWriter();

        ComukiInit.Run(["init"], writer, workingDirectory);

        var envFile = EnvFileContent();
        var apiKeyLine = envFile.Split('\n').Single(static line => line.StartsWith("COMUKI_IDENTITY_APIKEY_PEPPER=", StringComparison.Ordinal));
        var tokenLine = envFile.Split('\n').Single(static line => line.StartsWith("COMUKI_TOKEN_PEPPER=", StringComparison.Ordinal));

        var apiKeyPepper = apiKeyLine["COMUKI_IDENTITY_APIKEY_PEPPER=".Length..];
        var tokenPepper = tokenLine["COMUKI_TOKEN_PEPPER=".Length..];
        apiKeyPepper.Length.ShouldBe(64);
        tokenPepper.Length.ShouldBe(64);
        apiKeyPepper.ShouldNotBe(tokenPepper);
        apiKeyPepper.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact(DisplayName = "Given existing files, when init runs without --force, then files are skipped and exit is 1")]
    public void SkipExistingFilesWithoutForce()
    {
        File.WriteAllText(Path.Combine(workingDirectory, ComukiInit.ConfigFileName), "keep me");
        var writer = new LineWriter();

        var exitCode = ComukiInit.Run(["init"], writer, workingDirectory);

        exitCode.ShouldBe(1);
        writer.Lines.ShouldContain($"skipped file={ComukiInit.ConfigFileName} reason=exists (use --force to overwrite)");
        File.ReadAllText(Path.Combine(workingDirectory, ComukiInit.ConfigFileName)).ShouldBe("keep me");
    }

    [Fact(DisplayName = "Given existing files, when init runs with --force, then files are overwritten and exit is 0")]
    public void OverwriteExistingFilesWithForce()
    {
        File.WriteAllText(Path.Combine(workingDirectory, ComukiInit.ConfigFileName), "stale");
        var writer = new LineWriter();

        var exitCode = ComukiInit.Run(["init", "--force"], writer, workingDirectory, static () => "cafe01");

        exitCode.ShouldBe(0);
        File.ReadAllText(Path.Combine(workingDirectory, ComukiInit.ConfigFileName)).ShouldContain("comuki config.toml");
    }

    [Fact(DisplayName = "Given an unknown flag, when init runs, then a usage hint is printed and exit is 2")]
    public void RejectUnknownFlags()
    {
        var writer = new LineWriter();

        var exitCode = ComukiInit.Run(["init", "--wizard"], writer, workingDirectory);

        exitCode.ShouldBe(2);
        writer.Lines.ShouldContain("usage: comuki init [--env <development|production>] [--force]");
    }

    [Fact(DisplayName = "Given --env without a value, when the options are parsed, then the parse fails")]
    public void RejectEnvFlagWithoutValue()
    {
        ComukiInitOptions.Parse(["init", "--env"]).ShouldBeNull();
        ComukiInitOptions.Parse(["init", "--env", " "]).ShouldBeNull();
    }

    [Fact(DisplayName = "Given flags in any order, when the options are parsed, then both are captured")]
    public void ParseFlagsInAnyOrder()
    {
        var options = ComukiInitOptions.Parse(["init", "--force", "--env", "staging"]);

        options.ShouldNotBeNull();
        options.Environment.ShouldBe("staging");
        options.Force.ShouldBeTrue();
    }

    private string EnvFileContent()
    {
        return File.ReadAllText(Path.Combine(workingDirectory, ComukiInit.EnvFileName));
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
