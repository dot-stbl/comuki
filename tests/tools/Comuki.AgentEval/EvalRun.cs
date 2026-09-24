using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.History;
using Comuki.AgentEval.Judges;
using Comuki.AgentEval.Pi;
using Comuki.AgentEval.Reporting;
using Comuki.AgentEval.Scoring;
using Comuki.AgentTest.Runner.Execution.Budget;
using Comuki.AgentTest.Runner.Scenarios;

namespace Comuki.AgentEval;

/// <summary>
/// Configuration for <see cref="EvalRun.RunAsync"/>: the corpus to
/// drive, the model-server mode, the live upstream (when mode is
/// <c>live</c>), the resolved budget cap, the path to the installed
/// real-pi binary, and the output base path for the JSON+markdown
/// report.
/// </summary>
/// <param name="CorpusDirectory">Repo-absolute path to the corpus directory.</param>
/// <param name="Mode">Which model server mode to run: fake | replay | live.</param>
/// <param name="BudgetCap">Effective per-run USD cap (smaller of scenario cap + env var). Null = unlimited.</param>
/// <param name="LiveUpstreamBaseUrl">When mode is live, the upstream URL the recorder forwards to. Null for fake/replay.</param>
/// <param name="LiveUpstreamToken">Optional bearer/API key stamped into <c>ANTHROPIC_AUTH_TOKEN</c> for live mode.</param>
/// <param name="PiExecutablePath">Absolute path to the installed real-pi binary. Empty when fake/replay mode runs without a real pi (see <see cref="EvalRun.RunAsync"/> for the gate).</param>
/// <param name="OutputBasePath">Base path (no extension) the JSON+markdown report is written to.</param>
/// <param name="JudgeClient">Optional LLM-as-judge client. Null when no live env is configured.</param>
public sealed record EvalRunOptions(
    string CorpusDirectory,
    ScenarioModelMode Mode,
    BudgetCap BudgetCap,
    Uri? LiveUpstreamBaseUrl,
    string? LiveUpstreamToken,
    string PiExecutablePath,
    string OutputBasePath,
    ILlmJudgeClient? JudgeClient);

/// <summary>
/// The orchestration glue <see cref="Program"/> calls into. Loads
/// the corpus, drives each entry through <see cref="PiEvalRunner"/>
/// + <see cref="DeterministicJudges"/> + <see cref="LlmJudge"/> +
/// <see cref="EvalScorer"/>, accumulates <see cref="EvalEntryResult"/>s,
/// builds the <see cref="EvalReport"/>, writes it, appends history,
/// cleans up scratch dirs, and returns the report.
/// </summary>
public static class EvalRun
{
    /// <summary>
    /// Per-entry wall-clock timeout. Generous-but-bounded: real pi in
    /// scripted/replay modes runs locally and finishes in seconds;
    /// live mode may take longer. Five minutes is the upper bound this
    /// tool ever waits for one entry before declaring it timed-out and
    /// continuing with whatever transcript was collected.
    /// </summary>
    public static TimeSpan PerEntryTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Runs the corpus end-to-end and returns the final report.</summary>
    public static async Task<EvalReport> RunAsync(EvalRunOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var corpus = CorpusLoader.LoadDirectory(options.CorpusDirectory);
        var repositoryRoot = LocateRepoRoot();
        var piRunner = new PiEvalRunner(repositoryRoot);
        var budgetTracker = new BudgetTracker(options.BudgetCap);

        var entries = new List<EvalEntryResult>(corpus.Count);
        foreach (var corpusEntry in corpus)
        {
            ct.ThrowIfCancellationRequested();

            var entryResult = await RunSingleEntryAsync(
                corpusEntry,
                piRunner,
                options,
                budgetTracker,
                ct).ConfigureAwait(false);

            entries.Add(entryResult);

            try { Directory.Delete(entryResult.ArtifactPaths[0], recursive: true); }
            catch { /* best-effort cleanup; report keeps the path even if rm fails */ }
        }

        stopwatch.Stop();
        var report = EvalReport.FromResults(
            options.Mode.ToString().ToLowerInvariant(),
            options.CorpusDirectory,
            startedAt,
            stopwatch.Elapsed,
            entries);

        await EvalReportWriter.WriteAsync(report, options.OutputBasePath, ct).ConfigureAwait(false);
        var historyPath = Path.Combine(repositoryRoot, "artifacts", "agent-eval", "history.jsonl");
        await HistoryAppender.AppendAsync(historyPath, report, ct).ConfigureAwait(false);

        return report;
    }

    private static async Task<EvalEntryResult> RunSingleEntryAsync(
        CorpusEntry corpusEntry,
        PiEvalRunner piRunner,
        EvalRunOptions options,
        BudgetTracker budgetTracker,
        CancellationToken ct)
    {
        RunTranscript transcript;
        string workingDirectory;
        string pristineFixtureDirectory;
        try
        {
            (transcript, workingDirectory, pristineFixtureDirectory) = await piRunner.RunAsync(
                corpusEntry,
                options.PiExecutablePath,
                options.Mode,
                options.LiveUpstreamBaseUrl,
                options.LiveUpstreamToken,
                budgetTracker,
                PerEntryTimeout,
                ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            transcript = new RunTranscript([], [], [], 0m, 0L, ExitedCleanly: false);
            workingDirectory = string.Empty;
            pristineFixtureDirectory = string.Empty;
            return BuildFailureEntry(corpusEntry, transcript, options, exception.Message);
        }

        var deterministic = new List<DeterministicVerdict>
        {
            await DeterministicJudges.EvaluateDiffAppliesAsync(
                workingDirectory,
                pristineFixtureDirectory,
                corpusEntry.Scenario.Assertions.Diff,
                ct).ConfigureAwait(false),

            DeterministicJudges.EvaluateFilesTouchedWithinAllowedSet(
                transcript.TouchedFilePaths,
                corpusEntry.Eval.ResolvedAllowedFiles),

            DeterministicJudges.EvaluateToolCallCount(transcript.ToolCallNames.Count, corpusEntry.Eval.MaxToolCalls),

            DeterministicJudges.EvaluateCostFromUsage(
                transcript.CostUsd,
                corpusEntry.Scenario.Assertions.Cost?.MaxUsdMicros),

            await DeterministicJudges.EvaluateTestsPassAsync(
                corpusEntry.Eval.ResolvedTestCommand,
                workingDirectory,
                PerEntryTimeout,
                ct).ConfigureAwait(false),
        };

        var transcriptSummary = BuildTranscriptSummary(transcript);
        var judge = await LlmJudge.EvaluateAsync(
            options.JudgeClient,
            corpusEntry,
            transcriptSummary,
            ct).ConfigureAwait(false);

        var score = EvalScorer.Score(corpusEntry, deterministic, judge);
        var cost = new Comuki.AgentTest.Runner.Reporting.Report.RunCost
        {
            UsdMicros = (long)decimal.Round(transcript.CostUsd * 1_000_000m, MidpointRounding.AwayFromZero),
        };

        return new EvalEntryResult
        {
            ScenarioName = corpusEntry.Scenario.Name,
            Difficulty = corpusEntry.Eval.Difficulty,
            Passed = score.Passed,
            Score = score,
            Cost = cost,
            DurationMs = transcript.DurationMs,
            ArtifactPaths = [workingDirectory],
        };
    }

    private static EvalEntryResult BuildFailureEntry(
        CorpusEntry corpusEntry,
        RunTranscript transcript,
        EvalRunOptions options,
        string failureMessage)
    {
        var deterministic = new List<DeterministicVerdict>
        {
            new()
            {
                JudgeName = "pi-run",
                Passed = false,
                Message = failureMessage,
            },
        };

        var score = EvalScorer.Score(corpusEntry, deterministic, JudgeOutcome.Error(failureMessage));
        return new EvalEntryResult
        {
            ScenarioName = corpusEntry.Scenario.Name,
            Difficulty = corpusEntry.Eval.Difficulty,
            Passed = score.Passed,
            Score = score,
            Cost = new Comuki.AgentTest.Runner.Reporting.Report.RunCost(),
            DurationMs = transcript.DurationMs,
            ArtifactPaths = [],
        };
    }

    private static string BuildTranscriptSummary(RunTranscript transcript)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("Tool calls (").Append(transcript.ToolCallNames.Count).Append("):");
        foreach (var toolCall in transcript.ToolCallNames)
        {
            builder.Append(' ').Append(toolCall);
        }

        builder.Append("\nTouched files (").Append(transcript.TouchedFilePaths.Count).Append("):");
        foreach (var touchedFile in transcript.TouchedFilePaths)
        {
            builder.Append(' ').Append(touchedFile);
        }

        builder.Append("\nCost: $").Append(transcript.CostUsd.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(" · Duration: ").Append(transcript.DurationMs).Append("ms");
        builder.Append(" · ExitedCleanly: ").Append(transcript.ExitedCleanly);
        return builder.ToString();
    }

    private static string LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "comuki.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("could not locate the repo root (comuki.slnx) from " + AppContext.BaseDirectory);
    }
}
