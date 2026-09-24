namespace Comuki.TestFakePi;

/// <summary>
/// Fake <c>pi</c> for integration tests: mimics
/// <c>pi -p PROMPT --mode json --no-session</c> by emitting the pi-native
/// json event stream — a session header, text deltas, a tool call, the
/// authoritative message_end, agent_end — read from the bundled Fixtures/
/// directory, one JSON object per line. Exits 0. Honors
/// <c>--fixtures-dir=PATH</c> (forwarded via the prompt args) to emit a
/// custom stream, and <c>--exit-code=N</c> to exercise the failure path.
/// When <c>ANTHROPIC_AUTH_TOKEN</c> is set in its environment (the
/// Translator stamps it per execution — issue #122), also writes
/// <c>fake-pi-env.json</c> into the working directory reporting the
/// model-gateway env it received and, when
/// <c>PI_CODING_AGENT_DIR</c> is set, the contents of
/// <c>$PI_CODING_AGENT_DIR/models.json</c> (issue #150), so tests can
/// assert the stamp and the per-execution models.json reached the child
/// process without polluting the streamed/journaled output.
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

        DumpModelGatewayEnvironment();

        foreach (var file in Directory.EnumerateFiles(fixturesDir, "*.json").Order(StringComparer.Ordinal))
        {
            await Console.Out.WriteLineAsync(await File.ReadAllTextAsync(file));
        }

        await Console.Out.WriteLineAsync(/*lang=json,strict*/ """{"type":"agent_end","messages":[]}""");

        return ExtractOption(args, "--exit-code=") is { } exitFlag && int.TryParse(exitFlag, out var forcedExit)
            ? forcedExit
            : 0;
    }

    /// <summary>
    /// Dumps the env only when the minted-token stamp is present, so
    /// suites that never stamp see no file. Values are test-controlled
    /// (base URL + base64url token) — no JSON escaping needed. Also
    /// snapshots <c>PI_CODING_AGENT_DIR</c> + the contents of its
    /// <c>models.json</c> when present, so tests can prove the
    /// per-execution proxy routing (issue #150) reached the child.
    /// </summary>
    private static void DumpModelGatewayEnvironment()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN")))
        {
            return;
        }

        var baseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        var token = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        var piCodingAgentDir = Environment.GetEnvironmentVariable("PI_CODING_AGENT_DIR");
        var modelsJsonContent = "<unset>";
        if (!string.IsNullOrEmpty(piCodingAgentDir))
        {
            var modelsJsonPath = Path.Combine(piCodingAgentDir, "models.json");
            modelsJsonContent = File.Exists(modelsJsonPath)
                ? File.ReadAllText(modelsJsonPath)
                : "<missing>";
        }

        // Serialized via JsonSerializer rather than raw string interpolation:
        // piCodingAgentDir is a Windows path with single backslashes (e.g.
        // "...\Users\..."), and naive "{{value}}" interpolation produced
        // invalid JSON (`\U` read as an escape sequence) on this OS.
        File.WriteAllText(
            Path.Combine(Directory.GetCurrentDirectory(), "fake-pi-env.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                anthropicBaseUrl = string.IsNullOrEmpty(baseUrl) ? "<unset>" : baseUrl,
                anthropicAuthToken = string.IsNullOrEmpty(token) ? "<unset>" : token,
                piCodingAgentDir = string.IsNullOrEmpty(piCodingAgentDir) ? "<unset>" : piCodingAgentDir,
                modelsJson = modelsJsonContent,
            }));
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
