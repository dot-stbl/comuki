#!/usr/bin/env node
/**
 * live-eval — WS9 manual/local entry point for `model.mode: live` scenarios.
 * Drives a real-pi translator cycle through a recording CassetteModelServer
 * pointed at the live upstream the calling environment names (the hapy
 * gateway in real use, a scripted FakeModelServer for local/CI verification
 * — never hardcoded here), enforces the runner-side budget cap
 * (`Comuki.AgentTest.Runner.Execution.Budget.BudgetCap` /
 * `tests/integration/Comuki.EndToEnd.AgentLoop/RealPi/LiveModePiFakeModelHarness.cs`),
 * and prints the same JSON+markdown report every tier's runner writes
 * (design.md "Report format for agents").
 *
 * Usage:
 *   node scripts/ci/live-eval.mjs --budget=<usd>
 *   node scripts/ci/live-eval.mjs --budget=<usd> --scenario <file>
 *
 * Zero npm dependencies on purpose: the script has to work on a bare clone
 * with nothing installed but node. Same pure-functions-plus-thin-CLI shape
 * as scripts/ci/record-cassette.mjs — read that file first, this one
 * mirrors its structure closely.
 *
 * CRITICAL: this script never reads a paid-API credential from anywhere
 * other than the calling environment, and never fabricates one. When
 * COMUKI_LIVE_MODEL_BASE_URL is not already set in the environment this
 * script runs in, live-eval prints a clear skip message and exits 0 — a
 * missing live target is not a script failure, matching the .NET side's
 * own Assert.SkipUnless behavior (LiveModeScenarioShould).
 *
 * Env vars this script reads from ITS OWN calling environment and passes
 * straight through to the child `dotnet run` process (never modified,
 * never logged — see the token-masking notes below):
 *   COMUKI_LIVE_MODEL_BASE_URL   the live upstream's base URL
 *   COMUKI_LIVE_MODEL_TOKEN      the bearer/API token for that upstream
 *
 * Env vars this script itself sets on the child process:
 *   COMUKI_LIVE_BUDGET_MAX_USD   from --budget (a process-wide ceiling —
 *                                 combines with the scenario's own
 *                                 budget.maxUsd via BudgetCap.Resolve,
 *                                 whichever is smaller wins)
 *   COMUKI_LIVE_CASSETTE_PATH    a scratch path this script picks (a live
 *                                 run's transcript is never a committed
 *                                 fixture — see record-cassette.mjs for
 *                                 the human-reviewed re-record flow)
 *   COMUKI_LIVE_REPORT_PATH      a scratch base path this script picks and
 *                                 then reads the resulting .json/.md back
 *                                 from
 */
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Absolute path to the integration test csproj this script drives. */
const DEFAULT_PROJECT_PATH = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '..', '..', 'tests', 'integration', 'Comuki.EndToEnd.AgentLoop', 'Comuki.EndToEnd.AgentLoop.csproj',
);

/**
 * The .NET fact `runLiveEval` filters the run down to, via Microsoft
 * Testing Platform's `--filter-class` — same mechanism and same reasoning
 * as scripts/ci/record-cassette.mjs's RECORD_FACT_FILTER: running the
 * whole suite unfiltered puts more than one real-pi fact through the same
 * shared RealPiFakeModelCollection process, which is both slower and (per
 * the orchestrator's own verification while landing WS8) a known source of
 * cross-fact interaction that has nothing to do with this fact's own
 * correctness.
 */
const LIVE_FACT_FILTER = 'Comuki.EndToEnd.AgentLoop.RealPi.LiveModeScenarioShould';

/** Env var live-eval reads (never writes) — the live upstream's base URL. Absence means "skip". */
const LIVE_BASE_URL_ENV_VAR = 'COMUKI_LIVE_MODEL_BASE_URL';

/** Env var live-eval reads (never writes, never logs) — the live upstream's auth token. */
const LIVE_TOKEN_ENV_VAR = 'COMUKI_LIVE_MODEL_TOKEN';

/** Default scenario when `--scenario` is not passed. */
const DEFAULT_SCENARIO_RELATIVE_PATH = join('tests', 'fixtures', 'scenarios', 'small-repo', 'add-null-check-live.scenario.yaml');

const USAGE = `live-eval — WS9 manual/local entry point for live-mode scenario runs

Usage:
  node scripts/ci/live-eval.mjs --budget=<usd> [--scenario <file>]
  node scripts/ci/live-eval.mjs --help

Arguments:
  --budget=<usd>     required; a non-negative decimal — the process-wide
                      live-mode budget ceiling (COMUKI_LIVE_BUDGET_MAX_USD).
                      Combines with the scenario's own budget.maxUsd; the
                      smaller of the two wins.
  --scenario <file>  optional; absolute or repo-relative path to the
                      .scenario.yaml to run. Defaults to
                      ${DEFAULT_SCENARIO_RELATIVE_PATH.replace(/\\\\/g, '/')}

Environment (read from the calling shell, never written by this script):
  COMUKI_LIVE_MODEL_BASE_URL   the live upstream's base URL. NOT SET means
                                this command prints a skip message and
                                exits 0 — it never fails just because the
                                live target isn't configured.
  COMUKI_LIVE_MODEL_TOKEN      the live upstream's auth token (optional;
                                an unset value still runs, against
                                whatever auth-free/placeholder behavior the
                                pointed-at upstream accepts).

Notes:
  - This command drives a REAL model call whenever COMUKI_LIVE_MODEL_BASE_URL
    is set — never invoke it with that variable pointed at a paid API
    unless you mean to spend the budget you passed. CI never sets this
    variable; only a human (or an explicit, deliberate qa:live-eval job)
    does.
  - The live run's cassette is a scratch artifact, not a committed fixture
    — use scripts/ci/record-cassette.mjs to produce a reviewed, committed
    cassette instead.
`;

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

/**
 * Parses CLI arguments. Returns one of:
 *   - { kind: 'usage', exitCode, message }
 *   - { kind: 'args', options }
 *   - { kind: 'error', message }
 *
 * @param {string[]} argv
 * @returns {{kind: 'usage', exitCode: number, message: string} | {kind: 'args', options: {budgetUsd: string, scenarioPath: string | null}} | {kind: 'error', message: string}}
 */
export function parseArgs(argv) {
  if (!Array.isArray(argv) || argv.length === 0) {
    return { kind: 'usage', exitCode: 1, message: USAGE };
  }

  if (argv.includes('--help') || argv.includes('-h')) {
    return { kind: 'usage', exitCode: 0, message: USAGE };
  }

  const budgetFlag = argv.find((arg) => arg === '--budget' || arg.startsWith('--budget='));
  let budgetRaw = null;
  if (budgetFlag !== undefined) {
    if (budgetFlag.includes('=')) {
      budgetRaw = budgetFlag.slice(budgetFlag.indexOf('=') + 1);
    }
    else {
      const index = argv.indexOf(budgetFlag);
      const next = argv[index + 1];
      budgetRaw = next !== undefined && !next.startsWith('--') ? next : null;
    }
  }

  const scenarioIndex = argv.indexOf('--scenario');
  let scenarioPath = null;
  if (scenarioIndex !== -1) {
    const next = argv[scenarioIndex + 1];
    scenarioPath = next !== undefined && !next.startsWith('--') ? next : null;
  }

  const errors = [];
  if (budgetRaw === null || budgetRaw.trim() === '') {
    errors.push('--budget=<usd> is required (a non-negative decimal live-mode budget ceiling)');
  }
  else {
    const parsed = Number(budgetRaw);
    if (!Number.isFinite(parsed) || parsed < 0) {
      errors.push(`--budget must be a non-negative number, got '${budgetRaw}'`);
    }
  }

  if (errors.length > 0) {
    return { kind: 'error', message: errors.join('\n') };
  }

  return {
    kind: 'args',
    options: { budgetUsd: budgetRaw, scenarioPath },
  };
}

/**
 * Resolves whether live-eval should run at all: reads
 * COMUKI_LIVE_MODEL_BASE_URL from the SUPPLIED env object (never the
 * script's own ambient `process.env` directly — keeps this function pure
 * and testable). Absence/blank is a clean skip, never an error.
 *
 * @param {NodeJS.ProcessEnv} env
 * @returns {{shouldRun: true} | {shouldRun: false, reason: string}}
 */
export function resolveActivation(env) {
  const baseUrl = env?.[LIVE_BASE_URL_ENV_VAR];
  if (baseUrl === undefined || baseUrl.trim() === '') {
    return {
      shouldRun: false,
      reason: `${LIVE_BASE_URL_ENV_VAR} is not set — live mode skipped (this is expected in CI; set it to a scripted FakeModelServer or a real gateway URL to run a live evaluation locally).`,
    };
  }

  return { shouldRun: true };
}

/**
 * Drives the .NET side: spawns `dotnet run` against the integration test
 * project, filtered to the live-mode fact, with the live env vars set.
 *
 * `runDotnet` defaults to {@link defaultRunDotnet}; tests pass a fake.
 *
 * @param {{scenarioPath: string | null, budgetUsd: string, cassettePath: string, reportBasePath: string, projectPath: string, env: NodeJS.ProcessEnv, runDotnet?: RunDotnet}} input
 * @returns {RunLiveEvalResult}
 */
export function runLiveEval(input) {
  const runDotnet = input.runDotnet ?? defaultRunDotnet;

  const env = {
    ...input.env,
    COMUKI_LIVE_BUDGET_MAX_USD: input.budgetUsd,
    COMUKI_LIVE_CASSETTE_PATH: input.cassettePath,
    COMUKI_LIVE_REPORT_PATH: input.reportBasePath,
  };

  const args = ['run', '--project', input.projectPath, '--no-build', '--', '--filter-class', LIVE_FACT_FILTER];
  return runDotnet(args, env);
}

/**
 * Default `runDotnet` implementation — the real `child_process.spawnSync`
 * against `dotnet`. Exposed for tests that need to fake the child-process
 * boundary.
 *
 * @type {RunDotnet}
 */
export function defaultRunDotnet(args, env) {
  const result = spawnSync('dotnet', args, {
    env,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    maxBuffer: 64 * 1024 * 1024,
  });

  return {
    status: result.status,
    signal: result.signal,
    stdout: result.stdout ?? '',
    stderr: result.stderr ?? '',
    error: result.error ?? null,
  };
}

/**
 * Reads the markdown report `LiveModeScenarioShould` wrote to
 * `<reportBasePath>.md` after a successful run. Returns null (not a throw)
 * when the file isn't there — the caller decides how to report that.
 *
 * @param {string} reportBasePath
 * @param {typeof readFileSync} [reader]
 * @returns {string | null}
 */
export function readReportMarkdown(reportBasePath, reader = readFileSync) {
  const markdownPath = `${reportBasePath}.md`;
  if (!existsSync(markdownPath)) {
    return null;
  }

  try {
    return reader(markdownPath, 'utf8');
  }
  catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

/**
 * @typedef {{
 *   status: number | null,
 *   signal: NodeJS.Signals | null,
 *   stdout: string,
 *   stderr: string,
 *   error: Error | null,
 * }} RunLiveEvalResult
 *
 * @typedef {(args: string[], env: NodeJS.ProcessEnv) => RunLiveEvalResult} RunDotnet
 */

/**
 * Top-level CLI entrypoint. Returns a process exit code; never calls
 * `process.exit` itself so callers (tests) can drive it without side
 * effects.
 *
 * @param {string[]} argv
 * @param {{runDotnet?: RunDotnet, readFileSync?: typeof readFileSync, stdout?: NodeJS.WritableStream, stderr?: NodeJS.WritableStream, projectPath?: string, env?: NodeJS.ProcessEnv, scratchDir?: string}} [deps]
 * @returns {number}
 */
export function main(argv, deps = {}) {
  const stdout = deps.stdout ?? process.stdout;
  const stderr = deps.stderr ?? process.stderr;
  const runDotnet = deps.runDotnet;
  const reader = deps.readFileSync ?? readFileSync;
  const projectPath = deps.projectPath ?? DEFAULT_PROJECT_PATH;
  const env = deps.env ?? process.env;

  const parsed = parseArgs(argv);
  if (parsed.kind === 'usage') {
    stdout.write(parsed.message);
    return parsed.exitCode;
  }
  if (parsed.kind === 'error') {
    stderr.write(`${parsed.message}\n\n${USAGE}`);
    return 1;
  }

  const activation = resolveActivation(env);
  if (!activation.shouldRun) {
    stdout.write(`live-eval: skipped — ${activation.reason}\n`);
    return 0;
  }

  const hasToken = typeof env[LIVE_TOKEN_ENV_VAR] === 'string' && env[LIVE_TOKEN_ENV_VAR].trim() !== '';
  stdout.write(`live-eval: upstream = ${env[LIVE_BASE_URL_ENV_VAR]}\n`);
  stdout.write(`live-eval: token = ${hasToken ? '(set, not shown)' : '(not set — a placeholder token is stamped)'}\n`);
  stdout.write(`live-eval: budget = $${parsed.options.budgetUsd}\n`);

  const scenarioPath = parsed.options.scenarioPath !== null
    ? resolve(parsed.options.scenarioPath)
    : resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', DEFAULT_SCENARIO_RELATIVE_PATH);
  stdout.write(`live-eval: scenario = ${scenarioPath}\n`);

  const scratchDir = deps.scratchDir ?? mkdtempSync(join(tmpdir(), 'comuki-live-eval-'));
  const cassettePath = join(scratchDir, 'live-eval-cassette.json');
  const reportBasePath = join(scratchDir, 'live-eval-report');

  // IMPORTANT: the scratch dir holds the cassette AND the report
  // (reportBasePath) the .NET side writes — it must stay on disk until
  // AFTER readReportMarkdown runs below. An earlier version of this
  // function cleaned it up in a `finally` wrapped only around `runLiveEval`,
  // which deleted the report before it was ever read back (caught by the
  // orchestrator's own end-to-end verification against a real
  // FakeModelServer — the report always silently failed to print). Cleanup
  // now happens once, at the very end, after every read of scratchDir is done.
  const result = runLiveEval({
    scenarioPath: parsed.options.scenarioPath,
    budgetUsd: parsed.options.budgetUsd,
    cassettePath,
    reportBasePath,
    projectPath,
    env,
    runDotnet,
  });

  if (result.error !== null) {
    cleanupScratchDir(deps, scratchDir);
    stderr.write(`live-eval: failed to spawn dotnet: ${result.error.message}\n`);
    return 1;
  }

  const markdown = readReportMarkdown(reportBasePath, reader);
  if (markdown !== null) {
    stdout.write(`\n${markdown}\n`);
  }

  if (result.status !== 0) {
    stderr.write(`live-eval: dotnet run exited with status ${result.status}\n`);
    if (markdown === null) {
      if (result.stdout.length > 0) {
        stderr.write(`--- dotnet stdout (tail) ---\n${tailLines(result.stdout, 50)}\n`);
      }
      if (result.stderr.length > 0) {
        stderr.write(`--- dotnet stderr (tail) ---\n${tailLines(result.stderr, 50)}\n`);
      }
    }
    cleanupScratchDir(deps, scratchDir);
    return 1;
  }

  cleanupScratchDir(deps, scratchDir);

  stdout.write('live-eval: PASS\n');
  return 0;
}

function tailLines(text, lines) {
  const all = text.split(/\r?\n/);
  if (all.length <= lines) {
    return all.join('\n');
  }
  return all.slice(-lines).join('\n');
}

/**
 * Best-effort removal of the scratch dir `main` created for this run's
 * cassette + report — skipped when the caller (a test) supplied its own
 * `scratchDir`, since that directory isn't this function's to delete.
 *
 * @param {{scratchDir?: string}} deps
 * @param {string} scratchDir
 */
function cleanupScratchDir(deps, scratchDir) {
  if (deps.scratchDir !== undefined) {
    return;
  }

  try {
    rmSync(scratchDir, { recursive: true, force: true });
  }
  catch {
    // A leftover temp dir is not worth failing the command over.
  }
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  process.exitCode = main(process.argv.slice(2));
}
