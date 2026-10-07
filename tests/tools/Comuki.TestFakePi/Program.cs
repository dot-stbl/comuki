namespace Comuki.TestFakePi;

/// <summary>
/// Fake <c>pi</c> for integration tests: mimics
/// <c>pi -p PROMPT --mode json --no-session</c> by emitting the pi-native
/// json event stream — a session header, text deltas, a tool call, the
/// authoritative message_end, agent_end — read from the bundled Fixtures/
/// directory, one JSON object per line. Honors the exit-code contract
/// from two complementary surfaces: <c>--exit-code=N</c> on the command
/// line, and the <c>COMUKI_FAKE_PI_EXIT_CODE</c> environment variable.
/// <c>--exit-code</c> wins when set; the env var is the test-suite's
/// hook for <c>pi --mode rpc</c> runs (the production <c>PiHarness</c>
/// only forwards <c>--mode rpc --no-session</c> — no test-only args).
/// Mirrors the exit-code surface for stderr: when <c>COMUKI_FAKE_PI_STDERR</c>
/// is set in the environment, the value is written to stderr before the
/// session completes — the parent (<c>PiRpcSession</c>) captures the
/// stream, trims to a safe tail, and surfaces it through
/// <c>IHarnessSession.StderrTail</c>; the <c>PiPump</c> appends it to
/// the outcome's <c>ErrorText</c> on a non-zero exit so the
/// worker-runtime spec scenario "outcome is failed carrying the exit
/// code and stderr" is exercised end-to-end.
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
    /// <summary>Env var name a test sets to force a non-zero exit code from <c>PiHarness</c>-spawned runs.</summary>
    public const string ExitCodeEnvVar = "COMUKI_FAKE_PI_EXIT_CODE";

    /// <summary>Env var name a test sets to inject stderr on <c>PiHarness</c>-spawned runs (the harness's stderr reader drains and the PiPump appends it to the outcome's ErrorText on a non-zero exit).</summary>
    public const string StderrEnvVar = "COMUKI_FAKE_PI_STDERR";

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

        // stderr surface (mirrors the exit-code contract above): test
        // sets the env var in its own process, the child inherits it,
        // writes the value before the session closes, and the harness's
        // stderr reader captures it for the PiPump's outcome.
        WriteStderrIfRequested();

        // CLI flag first (existing contract), then env var (the hook the
        // worker-runtime exit-code harness test uses — PiHarness does
        // not forward any test-only CLI args, so the test sets the env
        // var in its own process and the child inherits it).
        return ResolveExitCode(args);
    }

    private static int ResolveExitCode(string[] args)
    {
        if (ExtractOption(args, "--exit-code=") is { } exitFlag && int.TryParse(exitFlag, out var cliExit))
        {
            return cliExit;
        }

        var envValue = Environment.GetEnvironmentVariable(ExitCodeEnvVar);
        return int.TryParse(envValue, out var envExit) ? envExit : 0;
    }

    /// <summary>
    /// Writes the <c>COMUKI_FAKE_PI_STDERR</c> env value to stderr when
    /// the test sets it; no-op otherwise. The harness's stderr reader
    /// captures the write, the PiRpcSession trims it to the safe-size
    /// tail cap (see <c>PiRpcSession.StderrTailMaxChars</c>), and the
    /// outcome's <c>ErrorText</c> reflects it on a non-zero exit.
    /// </summary>
    private static void WriteStderrIfRequested()
    {
        var stderr = Environment.GetEnvironmentVariable(StderrEnvVar);
        if (!string.IsNullOrEmpty(stderr))
        {
            Console.Error.WriteLine(stderr);
        }
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
