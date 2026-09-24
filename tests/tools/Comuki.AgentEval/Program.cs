using Comuki.AgentEval;
using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.History;
using Comuki.AgentEval.Judges;
using Comuki.AgentEval.Pi;
using Comuki.AgentEval.Reporting;
using Comuki.AgentEval.Scoring;
using Comuki.AgentTest.Runner.Execution.Budget;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Host.Translator.Parsing;

namespace Comuki.AgentEval;

/// <summary>
/// CLI entry point for the WS10 eval harness. Invocation:
/// <c>dotnet run --project tests/tools/Comuki.AgentEval -- --mode=fake|replay|live --corpus=DIR --budget-usd=N [--out=BASEPATH] [--history=PATH] [--filter=NAME] [--trend]</c>.
/// Mirrors the same flag-equals / env-var resolver pattern
/// <c>Comuki.TestFakeModel.Program</c> uses (matching CLI arg wins
/// over the env var; no external CLI-parsing package).
/// </summary>
/// <remarks>
/// <para>Defaults:
/// <list type="bullet">
///   <item><c>--corpus</c> — <c>tests/fixtures/scenarios/agent-eval</c>, resolved relative to the repo root (found by walking up for the <c>comuki.slnx</c> marker file).</item>
///   <item><c>--out</c> — <c>artifacts/agent-eval/[UTC-yyyyMMddTHHmmssZ]-report</c>.</item>
///   <item><c>--history</c> — <c>artifacts/agent-eval/history.jsonl</c>.</item>
///   <item><c>--budget-usd</c> unset — <see cref="BudgetCap.Unlimited"/> via <see cref="BudgetCap.Resolve"/>(null, <see cref="Comuki.AgentTest.Runner.Execution.ScenarioRunner.GlobalBudgetEnvVar"/>).</item>
/// </list>
/// </para>
/// <para>
/// <c>--mode=live</c> requires <c>COMUKI_LIVE_MODEL_BASE_URL</c> to be set;
/// if absent, prints a clean skip message and returns 0. <c>fake</c> and
/// <c>replay</c> never require those env vars.
/// </para>
/// <para>
/// <c>--trend</c> alone (no run) reads <c>--history</c> and prints a
/// plain-text table (timestamp | mode | corpus | pass/total |
/// avgQualityScore | costUsd) to stdout, then exits 0.
/// </para>
/// </remarks>
public static class Program
{
    private const string DefaultCorpusRelativePath = "tests/fixtures/scenarios/agent-eval";
    private const string DefaultHistoryRelativePath = "artifacts/agent-eval/history.jsonl";
    private const string DefaultRepoRootMarker = "comuki.slnx";

    private const string EnvLiveModelBaseUrl = "COMUKI_LIVE_MODEL_BASE_URL";
    private const string EnvLiveModelToken = "COMUKI_LIVE_MODEL_TOKEN";

    private const string UsageText = """
        Comuki.AgentEval — WS10 golden-task eval harness.

        Usage:
          dotnet run --project tests/tools/Comuki.AgentEval -- --mode=fake|replay|live
                                                                       [--corpus=<dir>] [--budget-usd=<n>] [--out=<basePath>]
                                                                       [--history=<path>] [--filter=<scenarioName>] [--trend]
          dotnet run --project tests/tools/Comuki.AgentEval -- --help

        Arguments:
          --mode=<m>           fake | replay | live — required (unless --trend is the only flag).
          --corpus=<dir>       repo-relative path to the corpus directory. Default: tests/fixtures/scenarios/agent-eval.
          --budget-usd=<n>     process-wide USD ceiling (smaller-of-two-wins with scenario.assertions.cost.maxUsdMicros).
                                Default: 0 (= unlimited, COMUKI_LIVE_BUDGET_MAX_USD also consulted).
          --out=<basePath>     base path (no extension) for the JSON+markdown report. Default: artifacts/agent-eval/<UTC-yyyyMMddTHHmmssZ>-report.
          --history=<path>     JSONL history file path. Default: artifacts/agent-eval/history.jsonl.
          --filter=<name>      exact-match filter on corpus entry name.
          --trend              if the only flag, reads --history and prints a plain-text trend table.

        Environment (read from the calling shell, never written by this script):
          COMUKI_LIVE_MODEL_BASE_URL   the live upstream's base URL — required to activate --mode=live;
                                       absent = a clean skip message and exit 0.
          COMUKI_LIVE_MODEL_TOKEN      optional bearer/API token; unset stamps a placeholder the recorder accepts.
          COMUKI_LIVE_BUDGET_MAX_USD   alternative global ceiling (same smaller-of-two-wins semantic).

        Notes:
          - --mode=live drives a REAL model call. Never invoke with COMUKI_LIVE_MODEL_BASE_URL pointed
            at a paid API unless the budget you passed is meant to spend.
          - A live run's cassette is a scratch artifact, not a committed fixture.
        """;

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            await Console.Out.WriteLineAsync(UsageText).ConfigureAwait(false);
            return 0;
        }

        var modeRaw = Resolve(args, "--mode=");
        var corpusRaw = Resolve(args, "--corpus=");
        var budgetRaw = Resolve(args, "--budget-usd=");
        var outRaw = Resolve(args, "--out=");
        var historyRaw = Resolve(args, "--history=");
        var filterRaw = Resolve(args, "--filter=");
        var trendFlag = args.Contains("--trend");

        var repoRoot = LocateRepoRoot();
        if (repoRoot is null)
        {
            await Console.Error.WriteLineAsync(
                "could not locate the repo root (comuki.slnx) from " + AppContext.BaseDirectory).ConfigureAwait(false);
            return 1;
        }

        if (trendFlag && modeRaw is null)
        {
            var historyPath = ResolveRepoRelativePath(historyRaw, repoRoot, DefaultHistoryRelativePath);
            var entries = TrendReader.ReadTrend(historyPath);
            await Console.Out.WriteLineAsync(TrendReader.RenderTrendTable(entries)).ConfigureAwait(false);
            return 0;
        }

        if (modeRaw is null)
        {
            await Console.Error.WriteLineAsync("--mode=<fake|replay|live> is required (or pass --trend alone).").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(UsageText).ConfigureAwait(false);
            return 1;
        }

        if (!TryParseMode(modeRaw, out var mode))
        {
            await Console.Error.WriteLineAsync($"--mode='{modeRaw}' is not one of fake|replay|live").ConfigureAwait(false);
            return 1;
        }

        if (mode == ScenarioModelMode.Live && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvLiveModelBaseUrl)))
        {
            await Console.Out.WriteLineAsync(
                $"agent-eval: skipped — {EnvLiveModelBaseUrl} is not set (--mode=live requires it).").ConfigureAwait(false);
            return 0;
        }

        var corpusDirectory = ResolveRepoRelativePath(corpusRaw, repoRoot, DefaultCorpusRelativePath);
        var budgetCap = ResolveBudgetCap(budgetRaw);

        Uri? liveUpstreamBaseUrl = null;
        string? liveUpstreamToken = null;
        if (mode == ScenarioModelMode.Live)
        {
            var baseUrlRaw = Environment.GetEnvironmentVariable(EnvLiveModelBaseUrl)!;
            if (!Uri.TryCreate(baseUrlRaw, UriKind.Absolute, out liveUpstreamBaseUrl))
            {
                await Console.Error.WriteLineAsync($"{EnvLiveModelBaseUrl}='{baseUrlRaw}' is not a valid absolute URL").ConfigureAwait(false);
                return 1;
            }

            liveUpstreamToken = Environment.GetEnvironmentVariable(EnvLiveModelToken);
        }

        ILlmJudgeClient? judgeClient = null;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvLiveModelBaseUrl)))
        {
            try
            {
                judgeClient = new HapyLlmJudgeClient(
                    new Uri(Environment.GetEnvironmentVariable(EnvLiveModelBaseUrl)!),
                    Environment.GetEnvironmentVariable(EnvLiveModelToken));
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync($"failed to build HapyLlmJudgeClient: {exception.Message}").ConfigureAwait(false);
                return 1;
            }
        }

        var piInstall = await RealPiInstaller.InstallAsync(CancellationToken.None).ConfigureAwait(false);
        if (!piInstall.Succeeded)
        {
            await Console.Error.WriteLineAsync($"agent-eval: failed to install real pi: {piInstall.Reason}").ConfigureAwait(false);
            return 1;
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var outputBasePath = ResolveRepoRelativePath(
            outRaw,
            repoRoot,
            Path.Combine("artifacts", "agent-eval", $"{startedAtUtc:yyyyMMddTHHmmssZ}-report"));

        var options = new EvalRunOptions(
            CorpusDirectory: corpusDirectory,
            Mode: mode,
            BudgetCap: budgetCap,
            LiveUpstreamBaseUrl: liveUpstreamBaseUrl,
            LiveUpstreamToken: liveUpstreamToken,
            PiExecutablePath: piInstall.ExecutablePath,
            OutputBasePath: outputBasePath,
            JudgeClient: judgeClient);

        try
        {
            var report = await EvalRun.RunAsync(options, CancellationToken.None).ConfigureAwait(false);
            var verdict = EvalReportWriter.Verdict(report, outputBasePath + ".md");
            await Console.Out.WriteLineAsync(verdict).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            if (judgeClient is not null)
            {
                await judgeClient.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static string? Resolve(string[] args, string prefix)
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

    private static bool TryParseMode(string raw, out ScenarioModelMode mode)
    {
        switch (raw.ToLowerInvariant())
        {
            case "fake":
                mode = ScenarioModelMode.Fake;
                return true;
            case "replay":
                mode = ScenarioModelMode.Replay;
                return true;
            case "live":
                mode = ScenarioModelMode.Live;
                return true;
            default:
                mode = ScenarioModelMode.Fake;
                return false;
        }
    }

    private static BudgetCap ResolveBudgetCap(string? budgetRaw) =>
        BudgetCap.Resolve(
            decimal.TryParse(budgetRaw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null,
            Comuki.AgentTest.Runner.Execution.ScenarioRunner.GlobalBudgetEnvVar);

    /// <summary>
    /// Resolves a user-supplied path (<c>--corpus</c>/<c>--out</c>/<c>--history</c>)
    /// against <paramref name="repoRoot"/> when it's relative — NOT against
    /// <see cref="Environment.CurrentDirectory"/>, which <c>dotnet run</c>
    /// sets to the project directory rather than the caller's shell CWD
    /// (verified: a bare <c>Path.GetFullPath(raw)</c> here silently nested
    /// a relative <c>--corpus</c> value under
    /// <c>tests/tools/Comuki.AgentEval/</c> instead of the repo root, the
    /// exact bug <c>scripts/ci/agent-eval.mjs</c>'s smoke test caught).
    /// An absolute <paramref name="raw"/> value is used as-is. <paramref name="raw"/>
    /// null falls back to <paramref name="defaultRelativePath"/> under <paramref name="repoRoot"/>.
    /// </summary>
    private static string ResolveRepoRelativePath(string? raw, string repoRoot, string defaultRelativePath)
    {
        var candidate = raw is null
            ? Path.Combine(repoRoot, defaultRelativePath)
            : Path.IsPathRooted(raw) ? raw : Path.Combine(repoRoot, raw);
        return Path.GetFullPath(candidate);
    }

    private static string? LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, DefaultRepoRootMarker)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }
}
