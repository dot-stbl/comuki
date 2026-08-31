namespace Comuki.TestFakePi;

/// <summary>
/// Fake <c>pi</c> for integration tests: mimics
/// <c>pi -p PROMPT --mode json --no-session</c> by emitting the pi-native
/// json event stream — a session header, text deltas, a tool call, the
/// authoritative message_end, agent_end — read from the bundled Fixtures/
/// directory, one JSON object per line. Exits 0. Honors
/// <c>--fixtures-dir=PATH</c> (forwarded via the prompt args) to emit a
/// custom stream, and <c>--exit-code=N</c> to exercise the failure path.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var fixturesDir = ExtractOption(args, "--fixtures-dir=") ?? DefaultFixturesDir();
        if (!Directory.Exists(fixturesDir))
        {
            await Console.Error.WriteLineAsync($"[TestFakePi] fixtures dir not found: {fixturesDir}");
            return 1;
        }

        await Console.Out.WriteLineAsync(/*lang=json,strict*/ """{"type":"session","version":3,"id":"0f1e2d3c-4b5a-6978-8776-655443332211","timestamp":"2026-08-31T12:00:00.000Z","cwd":"/work"}""");

        foreach (var file in Directory.EnumerateFiles(fixturesDir, "*.json").Order(StringComparer.Ordinal))
        {
            await Console.Out.WriteLineAsync(await File.ReadAllTextAsync(file));
        }

        await Console.Out.WriteLineAsync(/*lang=json,strict*/ """{"type":"agent_end","messages":[]}""");

        return ExtractOption(args, "--exit-code=") is { } exitFlag && int.TryParse(exitFlag, out var forcedExit)
            ? forcedExit
            : 0;
    }

    private static string? ExtractOption(string[] args, string prefix)
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

    private static string DefaultFixturesDir()
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures");
    }
}
