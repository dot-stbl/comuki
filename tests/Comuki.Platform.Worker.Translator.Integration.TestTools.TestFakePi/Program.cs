using System.Diagnostics.CodeAnalysis;

namespace Comuki.Platform.Worker.Translator.Integration.TestTools.TestFakePi;

/// <summary>
/// Fake <c>pi</c> for integration tests. Reads pre-recorded stream-json fixtures
/// (in a path passed as the first argument, or a default location) and writes
/// them to stdout, one JSON object per line. Mimics <c>pi -p PROMPT
/// --output-format stream-json</c> so the <c>IPiRunner</c> integration test
/// can validate the full .NET ↔ subprocess ↔ parser chain without a real
/// Anthropic-compatible API key.
/// </summary>
[ExcludeFromCodeCoverage]
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Honor pi's first-arg = prompt convention (we ignore it; this is fake pi).
        // PiRunner adds `-p PROMPT --output-format stream-json` to its invocation,
        // so we scan the args for a `--fixtures-dir=PATH` flag and otherwise use the
        // bundled Fixtures/ directory (the prompt itself is in args[0]).
        var prompt = args.Length > 0 ? args[0] : "(fake prompt)";
        var fixturesDir = ExtractFixturesDir(args) ?? DefaultFixturesDir();

        Console.Error.WriteLine($"[TestFakePi] prompt='{prompt}' fixtures='{fixturesDir}'");

        if (!Directory.Exists(fixturesDir))
        {
            Console.Error.WriteLine($"[TestFakePi] fixtures dir not found: {fixturesDir}");
            return 1;
        }

        foreach (var file in Directory.EnumerateFiles(fixturesDir, "event-*.json").Order(StringComparer.Ordinal))
        {
            var line = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            Console.Write(line);
        }

        // Mimic real pi: emit a final result event so the parser sees the
        // canonical end-of-session shape.
        Console.WriteLine("{\"type\":\"result\",\"subtype\":\"success\",\"duration_ms\":42,\"cost_usd\":0.0,\"result\":\"(fake pi done)\"}");
        return 0;
    }

    private static string? ExtractFixturesDir(string[] args)
    {
        foreach (var arg in args)
        {
            const string prefix = "--fixtures-dir=";
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    private static string DefaultFixturesDir()
    {
        // Bundled fixtures land here via the csproj's <None Include> with
        // CopyToOutputDirectory. Used when the test invokes TestFakePi without
        // an explicit --fixtures-dir=... flag.
        return Path.Combine(AppContext.BaseDirectory, "Fixtures");
    }
}
