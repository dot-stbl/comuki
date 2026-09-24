using Comuki.TestFakeModel.Cassettes.Hosting;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Loading;

namespace Comuki.TestFakeModel;

/// <summary>
/// Standalone host for <see cref="FakeModelServer"/> (mode <c>fake</c>)
/// and <see cref="CassetteModelServer"/> (modes <c>replay</c>/<c>record</c>)
/// — the manual-smoke and container-image (WS5, <c>Dockerfile</c>) entry
/// point; the same binary backs every tier (design.md's "Fake-model
/// design"). Every option is a <c>--flag=value</c> CLI arg or its
/// <c>COMUKI_TESTFAKEMODEL_*</c> env var equivalent — the container sets
/// env, a manual smoke run typically uses CLI args; a CLI arg always wins
/// over its env var.
/// </summary>
public static class Program
{
    private const int DefaultPort = 17190;

    /// <summary>
    /// Usage: <c>dotnet run --project tests/tools/Comuki.TestFakeModel --
    /// --mode=fake|replay|record [--script=PATH] [--cassette=PATH]
    /// [--upstream=URL] [--port=N] [--scenario-name=NAME]
    /// [--recorded-against=MODEL]</c> — or the equivalent
    /// <c>COMUKI_TESTFAKEMODEL_MODE</c>/<c>_SCRIPT</c>/<c>_CASSETTE</c>/
    /// <c>_UPSTREAM</c>/<c>_PORT</c>/<c>_SCENARIO</c>/<c>_RECORDED_AGAINST</c>
    /// env vars. <c>fake</c> (the default) needs no flags — it serves the
    /// bundled <c>Fixtures/hello.fake.json</c> when <c>--script</c> is
    /// omitted. <c>replay</c> requires <c>--cassette</c> (must exist).
    /// <c>record</c> requires both <c>--cassette</c> and <c>--upstream</c>.
    /// Runs until Ctrl+C / SIGTERM.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        var mode = ProgramOptions.Resolve(args, "--mode=", "COMUKI_TESTFAKEMODEL_MODE") ?? "fake";
        var port = ProgramOptions.Resolve(args, "--port=", "COMUKI_TESTFAKEMODEL_PORT") is { } portArg && int.TryParse(portArg, out var parsedPort)
            ? parsedPort
            : DefaultPort;

        if (mode.Equals("fake", StringComparison.OrdinalIgnoreCase))
        {
            return await ProgramRunner.RunFakeAsync(args, port);
        }

        if (mode.Equals("replay", StringComparison.OrdinalIgnoreCase))
        {
            return await ProgramRunner.RunCassetteAsync(args, port, CassetteModelMode.Replay);
        }

        if (mode.Equals("record", StringComparison.OrdinalIgnoreCase))
        {
            return await ProgramRunner.RunCassetteAsync(args, port, CassetteModelMode.Record);
        }

        await Console.Error.WriteLineAsync($"[TestFakeModel] unknown mode '{mode}' (expected fake|replay|record).");
        return 1;
    }
}

/// <summary>The CLI-argument/env-var resolution step <see cref="Program"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class ProgramOptions
{
    /// <summary>A matching <c>--flag=</c> arg wins; otherwise <paramref name="envVarName"/> when it's set to a non-empty value; otherwise <c>null</c>.</summary>
    public static string? Resolve(string[] args, string cliPrefix, string envVarName)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(cliPrefix, StringComparison.Ordinal))
            {
                return arg[cliPrefix.Length..];
            }
        }

        return Environment.GetEnvironmentVariable(envVarName) is { Length: > 0 } envValue ? envValue : null;
    }
}

/// <summary>The per-mode startup steps <see cref="Program"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class ProgramRunner
{
    public static async Task<int> RunFakeAsync(string[] args, int port, CancellationToken cancellationToken = default)
    {
        var scriptPath = ProgramOptions.Resolve(args, "--script=", "COMUKI_TESTFAKEMODEL_SCRIPT") ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "hello.fake.json");
        if (!File.Exists(scriptPath))
        {
            await Console.Error.WriteLineAsync($"[TestFakeModel] script file not found: {scriptPath}".AsMemory(), cancellationToken);
            return 1;
        }

        var script = FakeScriptLoader.LoadFromFile(scriptPath, ProgramOptions.Resolve(args, "--scenario-name=", "COMUKI_TESTFAKEMODEL_SCENARIO"));

        // Bind every interface, not just loopback: a container's loopback interface
        // isn't reachable through the host's port mapping — see FakeModelServerOptions.BindAddress.
        await using var server = new FakeModelServer(new FakeModelServerOptions { Script = script, Port = port, BindAddress = "0.0.0.0" });
        await server.StartAsync(cancellationToken);
        await Console.Out.WriteLineAsync(
            $"[TestFakeModel] mode=fake listening on {server.BaseAddress} (scenario={script.ScenarioName}, entries={script.Entries.Count})".AsMemory(), cancellationToken);

        await ProgramShutdown.WaitForSignalAsync();
        await server.StopAsync(cancellationToken);
        return 0;
    }

    public static async Task<int> RunCassetteAsync(string[] args, int port, CassetteModelMode mode, CancellationToken cancellationToken = default)
    {
        var cassettePath = ProgramOptions.Resolve(args, "--cassette=", "COMUKI_TESTFAKEMODEL_CASSETTE");
        if (cassettePath is null)
        {
            await Console.Error.WriteLineAsync($"[TestFakeModel] --cassette (or COMUKI_TESTFAKEMODEL_CASSETTE) is required for mode={mode.Value}.".AsMemory(), cancellationToken);
            return 1;
        }

        if (mode == CassetteModelMode.Replay && !File.Exists(cassettePath))
        {
            await Console.Error.WriteLineAsync($"[TestFakeModel] cassette file not found: {cassettePath}".AsMemory(), cancellationToken);
            return 1;
        }

        Uri? upstreamBaseUrl = null;
        if (mode == CassetteModelMode.Record)
        {
            var upstreamValue = ProgramOptions.Resolve(args, "--upstream=", "COMUKI_TESTFAKEMODEL_UPSTREAM");
            if (upstreamValue is null || !Uri.TryCreate(upstreamValue, UriKind.Absolute, out upstreamBaseUrl))
            {
                await Console.Error.WriteLineAsync("[TestFakeModel] --upstream (or COMUKI_TESTFAKEMODEL_UPSTREAM) must be an absolute URL for mode=record.".AsMemory(), cancellationToken);
                return 1;
            }
        }

        await using var server = new CassetteModelServer(new CassetteModelServerOptions
        {
            Mode = mode,
            CassettePath = cassettePath,
            Scenario = ProgramOptions.Resolve(args, "--scenario-name=", "COMUKI_TESTFAKEMODEL_SCENARIO") ?? "fake",
            RecordedAgainst = ProgramOptions.Resolve(args, "--recorded-against=", "COMUKI_TESTFAKEMODEL_RECORDED_AGAINST") ?? "unknown",
            UpstreamBaseUrl = upstreamBaseUrl,
            Port = port,
            BindAddress = "0.0.0.0",
        });
        await server.StartAsync(cancellationToken);
        await Console.Out.WriteLineAsync($"[TestFakeModel] mode={mode.Value} listening on {server.BaseAddress} (cassette={cassettePath})".AsMemory(), cancellationToken);

        await ProgramShutdown.WaitForSignalAsync();
        await server.StopAsync(cancellationToken);
        return 0;
    }
}

/// <summary>The Ctrl+C/SIGTERM wait <see cref="ProgramRunner"/>'s two run paths share — extracted per class-layout-and-tooling.md §1a.</summary>
file static class ProgramShutdown
{
    public static async Task WaitForSignalAsync()
    {
        var shutdown = new TaskCompletionSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.TrySetResult();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.TrySetResult();

        await shutdown.Task;
    }
}
