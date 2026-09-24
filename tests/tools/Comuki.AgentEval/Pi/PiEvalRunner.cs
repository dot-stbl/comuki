using System.Diagnostics;
using System.Text.Json;
using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.Judges;
using Comuki.AgentTest.Runner.Execution.Budget;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Host.Translator.Parsing;
using Comuki.TestFakeModel.Cassettes.Hosting;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Loading;
// Aliases disambiguate the two BudgetTracker / BudgetCap types — the
// runner-side and the TestFakeModel-local. We accept the runner-side
// type on the public PiEvalRunner API (matches the rest of the
// runner's vocabulary) and translate to the recorder-side type inside
// StartRecordingServerAsync.
using BudgetCap = Comuki.AgentTest.Runner.Execution.Budget.BudgetCap;
using BudgetTracker = Comuki.AgentTest.Runner.Execution.Budget.BudgetTracker;
using CassetteBudgetCap = Comuki.TestFakeModel.Cassettes.Hosting.BudgetCap;
using CassetteBudgetTracker = Comuki.TestFakeModel.Cassettes.Hosting.BudgetTracker;

namespace Comuki.AgentEval.Pi;

/// <summary>
/// One <c>pi</c> run's observed transcript: every emitted
/// <see cref="PiEvent"/>, the tool-call names extracted from them, the
/// repo-relative touched-file paths parsed from each tool call's args,
/// the run's USD cost from the terminal <see cref="PiEvent.ResultEvent"/>
/// (defaulting to zero when no result event arrived), the duration, and
/// a flag for "did the process exit cleanly before the deadline".
/// </summary>
public sealed record RunTranscript(
    IReadOnlyList<PiEvent> Events,
    IReadOnlyList<string> ToolCallNames,
    IReadOnlyList<string> TouchedFilePaths,
    decimal CostUsd,
    long DurationMs,
    bool ExitedCleanly);

/// <summary>
/// One corpus entry's per-run driver: copies the fixture to a scratch
/// dir, starts the mode-appropriate model server
/// (<c>FakeModelServer</c> / <c>CassetteModelServer</c> replay /
/// <c>CassetteModelServer</c> record), wires the <c>models.json</c> +
/// env-var stamp, spawns real <c>pi -p &lt;brief&gt;</c>, parses its
/// stdout via the existing <see cref="StreamJsonParser"/>, and returns
/// the transcript plus the working-directory paths the caller still
/// needs (judges read them; the caller deletes them when fully done).
/// </summary>
/// <remarks>
/// Bounded by <c>timeout</c>; on timeout, kills the pi process tree
/// and returns whatever transcript was collected so far with
/// <c>ExitedCleanly=false</c> rather than throwing — a timed-out scenario
/// becomes a normal failing report entry, not a crashed CLI run.
/// </remarks>
public sealed class PiEvalRunner(string repositoryRoot)
{
    /// <summary>
    /// Runs <paramref name="entry"/> against real <c>pi</c>, returning
    /// the observed transcript and the working-directory paths the
    /// caller's judges still need to read.
    /// </summary>
    /// <param name="entry">The corpus entry whose scenario is being driven.</param>
    /// <param name="piExecutablePath">Absolute path to the installed <c>pi</c> binary (from <see cref="RealPiInstaller"/>).</param>
    /// <param name="mode">Which model server mode to stand up: <c>fake</c>, <c>replay</c>, or <c>live</c>.</param>
    /// <param name="liveUpstreamBaseUrl">When <paramref name="mode"/> is <c>live</c>, the upstream URL the recording proxy forwards to. Null for fake/replay.</param>
    /// <param name="liveUpstreamToken">Optional bearer/API key stamped into <c>ANTHROPIC_AUTH_TOKEN</c> for live mode.</param>
    /// <param name="budgetTracker">Optional per-run budget tracker the recording path consults in live mode. Null for fake/replay.</param>
    /// <param name="timeout">Overall wall-clock cap; on timeout the runner returns a non-cleanly-exited transcript rather than throwing.</param>
    /// <param name="ct">Cancellation forwarded to the runner and to the pi process's WaitForExit.</param>
    public async Task<(RunTranscript transcript, string workingDirectory, string pristineFixtureDirectory)> RunAsync(
        CorpusEntry entry,
        string piExecutablePath,
        ScenarioModelMode mode,
        Uri? liveUpstreamBaseUrl,
        string? liveUpstreamToken,
        BudgetTracker? budgetTracker,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(piExecutablePath);

        var pristineFixtureDirectory = ResolveFixtureDirectory(entry.Scenario.Ticket.TargetRepo.Fixture);
        var workingDirectory = CopyFixtureRepoToScratch(entry.Scenario.Ticket.TargetRepo.Fixture);

        var agentDirectory = CreateScratchDirectory("agent-dir");

        FakeModelServer? fakeServer = null;
        CassetteModelServer? cassetteServer = null;
        Uri? modelBaseAddress = null;

        try
        {
            switch (mode)
            {
                case ScenarioModelMode.Fake:
                    fakeServer = await StartFakeServerAsync(entry, ct).ConfigureAwait(false);
                    modelBaseAddress = fakeServer.BaseAddress;
                    break;

                case ScenarioModelMode.Replay:
                    cassetteServer = await StartReplayServerAsync(entry, ct).ConfigureAwait(false);
                    modelBaseAddress = cassetteServer.BaseAddress;
                    break;

                case ScenarioModelMode.Live:
                    cassetteServer = await StartRecordingServerAsync(entry, liveUpstreamBaseUrl, budgetTracker, ct).ConfigureAwait(false);
                    modelBaseAddress = cassetteServer.BaseAddress;
                    break;

                default:
                    throw new InvalidOperationException($"unsupported model mode '{mode}'");
            }

            // Must happen AFTER the model server above has actually started —
            // only then is its real, dynamically-bound BaseAddress known. An
            // earlier revision wrote a placeholder URI here before any server
            // existed and never came back to overwrite it, which pointed real
            // pi at a closed port for the entire run (verified: this made
            // every request pi issued fail to connect, and pi does not fail
            // fast on that — it hangs well past any single-entry timeout).
            await WriteModelsJsonAsync(agentDirectory, modelBaseAddress!, ct).ConfigureAwait(false);

            var brief = ComposeBrief(entry);
            var (events, exitedCleanly, durationMs) = await SpawnPiAsync(
                piExecutablePath,
                workingDirectory,
                agentDirectory,
                brief,
                liveUpstreamToken,
                timeout,
                ct).ConfigureAwait(false);

            var transcript = SummarizeTranscript(events, durationMs, exitedCleanly);
            return (transcript, workingDirectory, pristineFixtureDirectory);
        }
        finally
        {
            await DisposeQuietlyAsync(fakeServer).ConfigureAwait(false);
            await DisposeQuietlyAsync(cassetteServer).ConfigureAwait(false);
            RestoreEnvironment();
        }
    }

    private string ResolveFixtureDirectory(string fixtureName)
    {
        var path = Path.Combine(repositoryRoot, "tests", "fixtures", "target-repos", fixtureName);
        return Directory.Exists(path)
            ? path
            : throw new InvalidOperationException($"target-repo fixture '{fixtureName}' not found at '{path}'");
    }

    private string CopyFixtureRepoToScratch(string fixtureName)
    {
        var source = ResolveFixtureDirectory(fixtureName);
        var target = CreateScratchDirectory("workdir");

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(target, relative), overwrite: true);
        }

        return target;
    }

    private static string CreateScratchDirectory(string suffix)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"comuki-agent-eval-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<FakeModelServer> StartFakeServerAsync(CorpusEntry entry, CancellationToken ct)
    {
        var model = entry.Scenario.Model ?? throw new InvalidOperationException(
            $"corpus entry '{entry.Scenario.Name}' declares model.mode: fake but has no model block");
        var fakeScriptPath = string.IsNullOrWhiteSpace(model.FakeScript)
            ? throw new InvalidOperationException(
                $"corpus entry '{entry.Scenario.Name}' declares model.mode: fake but has no model.fakeScript path")
            : ScenarioLoader.ResolveRelativeToScenario(entry.ScenarioPath, model.FakeScript);
        var script = FakeScriptLoader.LoadFromFile(fakeScriptPath, entry.Scenario.Name);
        var server = new FakeModelServer(new FakeModelServerOptions
        {
            Script = script,
            Port = null,
            BindAddress = "127.0.0.1",
        });
        await server.StartAsync(ct).ConfigureAwait(false);
        return server;
    }

    private static async Task<CassetteModelServer> StartReplayServerAsync(CorpusEntry entry, CancellationToken ct)
    {
        var model = entry.Scenario.Model ?? throw new InvalidOperationException(
            $"corpus entry '{entry.Scenario.Name}' declares model.mode: replay but has no model block");
        if (string.IsNullOrWhiteSpace(model.Cassette))
        {
            throw new InvalidOperationException(
                $"corpus entry '{entry.Scenario.Name}' declares model.mode: replay but has no model.cassette path");
        }

        var cassettePath = ScenarioLoader.ResolveRelativeToScenario(entry.ScenarioPath, model.Cassette);
        if (!File.Exists(cassettePath))
        {
            throw new InvalidOperationException(
                $"corpus entry '{entry.Scenario.Name}' declares model.cassette='{model.Cassette}' but no file exists at '{cassettePath}'");
        }

        var server = new CassetteModelServer(new CassetteModelServerOptions
        {
            Mode = CassetteModelMode.Replay,
            CassettePath = cassettePath,
            Scenario = entry.Scenario.Name,
            RecordedAgainst = "unknown",
            Port = null,
            BindAddress = "127.0.0.1",
        });
        await server.StartAsync(ct).ConfigureAwait(false);
        return server;
    }

    private static async Task<CassetteModelServer> StartRecordingServerAsync(
        CorpusEntry entry,
        Uri? liveUpstreamBaseUrl,
        BudgetTracker? budgetTracker,
        CancellationToken ct)
    {
        if (liveUpstreamBaseUrl is null)
        {
            throw new InvalidOperationException(
                $"corpus entry '{entry.Scenario.Name}' declares model.mode: live but no live upstream URL was supplied");
        }

        var scratchCassettePath = Path.Combine(
            Path.GetTempPath(),
            $"comuki-agent-eval-live-{entry.Scenario.Name}-{Guid.NewGuid():N}.json");

        var server = new CassetteModelServer(new CassetteModelServerOptions
        {
            Mode = CassetteModelMode.Record,
            CassettePath = scratchCassettePath,
            Scenario = entry.Scenario.Name,
            RecordedAgainst = $"recorded-live-upstream-{liveUpstreamBaseUrl.Host}",
            UpstreamBaseUrl = liveUpstreamBaseUrl,
            Port = null,
            BindAddress = "127.0.0.1",
            BudgetTracker = TranslateToRecorderTracker(budgetTracker),
        });
        await server.StartAsync(ct).ConfigureAwait(false);
        return server;
    }

    /// <summary>
    /// Writes the <c>providers.anthropic.baseUrl</c> override real pi reads
    /// from <c>PI_CODING_AGENT_DIR</c> (issue #150 — <c>ANTHROPIC_BASE_URL</c>
    /// alone does not redirect a cataloged Anthropic model). Must be called
    /// with the model server's ACTUAL <paramref name="modelBaseAddress"/> —
    /// after that server has started, never before.
    /// </summary>
    private static async Task WriteModelsJsonAsync(string agentDirectory, Uri modelBaseAddress, CancellationToken ct)
    {
        var json = $$"""
            {
              "providers": {
                "anthropic": {
                  "baseUrl": "{{modelBaseAddress}}"
                }
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(agentDirectory, "models.json"), json, ct).ConfigureAwait(false);
    }

    private static string ComposeBrief(CorpusEntry entry)
    {
        var ticket = entry.Scenario.Ticket;
        return $"{ticket.Title}\n\n{ticket.Body}";
    }

    private static async Task<(IReadOnlyList<PiEvent> events, bool exitedCleanly, long durationMs)> SpawnPiAsync(
        string piExecutablePath,
        string workingDirectory,
        string agentDirectory,
        string brief,
        string? liveToken,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(piExecutablePath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(brief);
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("--no-session");

        startInfo.EnvironmentVariables["PI_CODING_AGENT_DIR"] = agentDirectory;
        startInfo.EnvironmentVariables["ANTHROPIC_AUTH_TOKEN"] = string.IsNullOrWhiteSpace(liveToken)
            ? "real-pi-test-token"
            : liveToken;

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"failed to start '{piExecutablePath}' — process is null");
        }

        // pi's stdin is redirected above but this runner never writes to it —
        // an unclosed redirected stdin pipe can leave a headless Node/Bun CLI
        // blocked waiting on input that will never arrive (the same class of
        // hang the opencode CLI is documented to hit without `< /dev/null`).
        // Closing it immediately signals EOF, matching production PiRunner's
        // behavior of never redirecting stdin at all.
        process.StandardInput.Close();

        var stopwatch = Stopwatch.StartNew();
        var events = new List<PiEvent>();
        var exitedCleanly = false;

        var stdoutTask = Task.Run(async () =>
        {
            using var reader = process.StandardOutput;
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                foreach (var piEvent in StreamJsonParser.ParseLine(line))
                {
                    events.Add(piEvent);
                }
            }
        }, ct);

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            exitedCleanly = true;
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
        }
        finally
        {
            try { await stdoutTask.ConfigureAwait(false); }
            catch { /* ignore drain errors on cancellation */ }
        }

        stopwatch.Stop();

        return (events, exitedCleanly, stopwatch.ElapsedMilliseconds);
    }

    private static RunTranscript SummarizeTranscript(IReadOnlyList<PiEvent> events, long durationMs, bool exitedCleanly)
    {
        var toolCallNames = new List<string>();
        var touchedFiles = new List<string>();
        var costUsd = 0m;

        foreach (var piEvent in events)
        {
            switch (piEvent)
            {
                case PiEvent.AssistantToolUseEvent(var tool, var inputJson):
                    toolCallNames.Add(tool);
                    ExtractPathFromJson(inputJson, touchedFiles);
                    break;
                case PiEvent.ToolCallEvent(var toolName, var argsJson):
                    toolCallNames.Add(toolName);
                    ExtractPathFromJson(argsJson, touchedFiles);
                    break;
                case PiEvent.ResultEvent(_, _, var cost, _):
                    costUsd = cost;
                    break;
            }
        }

        return new RunTranscript(events, toolCallNames, touchedFiles, costUsd, durationMs, exitedCleanly);
    }

    private static void ExtractPathFromJson(string json, List<string> sink)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            WalkForPaths(document.RootElement, sink);
        }
        catch (JsonException)
        {
            // Best-effort — unknown shape is never an exception we propagate.
        }
    }

    private static void WalkForPaths(JsonElement element, List<string> sink)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var name = property.Name;
                    if (name is "path" or "file" or "filePath" or "file_path" or "filepath")
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            var raw = property.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(raw))
                            {
                                sink.Add(raw.Replace('\\', '/'));
                            }
                        }
                    }
                    else
                    {
                        WalkForPaths(property.Value, sink);
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    WalkForPaths(item, sink);
                }
                break;
        }
    }

    private static CassetteBudgetTracker? TranslateToRecorderTracker(BudgetTracker? runnerTracker) =>
        runnerTracker is null
            ? null
            : new CassetteBudgetTracker(
                runnerTracker.Cap.UsdMicros is { } micros
                    ? new CassetteBudgetCap(micros)
                    : CassetteBudgetCap.Unlimited);

    private static async Task DisposeQuietlyAsync(IAsyncDisposable? disposable)
    {
        if (disposable is null)
        {
            return;
        }

        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort teardown.
        }
    }

    private static void RestoreEnvironment()
    {
        // ProcessStartInfo.EnvironmentVariables only mutates the child
        // process's environment, not this process's — no restore needed.
        // This method exists to make the symmetry explicit and to provide
        // a future hook for cross-platform environment teardown.
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited between the check and the call — fine.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Best-effort teardown.
        }
    }
}
