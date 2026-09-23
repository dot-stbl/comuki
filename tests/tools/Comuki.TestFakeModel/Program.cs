using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Loading;

namespace Comuki.TestFakeModel;

/// <summary>
/// Standalone host for <see cref="FakeModelServer"/> — the manual-smoke
/// and container-image (WS5) entry point. <c>fake</c> is the only mode
/// this build serves; <c>replay</c>/<c>record</c> land in WS5.
/// </summary>
public static class Program
{
    private const int DefaultPort = 17190;

    /// <summary>
    /// Usage: <c>dotnet run --project tests/tools/Comuki.TestFakeModel --
    /// --mode=fake --script=PATH --port=N [--scenario-name=NAME]</c>.
    /// With no <c>--script</c>, serves the bundled
    /// <c>Fixtures/hello.fake.json</c> — a generous multi-turn "hello"
    /// script good enough for a quick manual smoke against a real
    /// <c>pi</c> binary. Runs until Ctrl+C / SIGTERM.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        var mode = ProgramArgs.ExtractOption(args, "--mode=") ?? "fake";
        if (!mode.Equals("fake", StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync(
                $"[TestFakeModel] mode '{mode}' isn't implemented yet — only 'fake' ships in WS4 (replay/record land in WS5).");
            return 1;
        }

        var scriptPath = ProgramArgs.ExtractOption(args, "--script=") ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "hello.fake.json");
        if (!File.Exists(scriptPath))
        {
            await Console.Error.WriteLineAsync($"[TestFakeModel] script file not found: {scriptPath}");
            return 1;
        }

        var script = FakeScriptLoader.LoadFromFile(scriptPath, ProgramArgs.ExtractOption(args, "--scenario-name="));

        await using var server = new FakeModelServer(new FakeModelServerOptions
        {
            Script = script,
            Port = ProgramArgs.ExtractOption(args, "--port=") is { } portArg && int.TryParse(portArg, out var parsedPort) ? parsedPort : DefaultPort,
        });

        await server.StartAsync();
        await Console.Out.WriteLineAsync(
            $"[TestFakeModel] listening on {server.BaseAddress} (scenario={script.ScenarioName}, entries={script.Entries.Count})");

        var shutdown = new TaskCompletionSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.TrySetResult();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.TrySetResult();

        await shutdown.Task;
        await server.StopAsync();
        return 0;
    }
}

/// <summary>The CLI-argument extraction step <see cref="Program"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class ProgramArgs
{
    public static string? ExtractOption(string[] args, string prefix)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }
}
