#!/usr/bin/env node
/**
 * agent-eval — WS10 MANUAL ENTRY POINT ONLY for the golden-task eval
 * harness (`Comuki.AgentEval`, tests/tools/Comuki.AgentEval). Never
 * invoked from .github/workflows/ci.yml or deploy/hybrid/ci.yml — CI
 * wiring is a separate, out-of-scope workstream (WS12).
 *
 * Zero npm dependencies, same pure-functions-plus-thin-CLI shape as
 * scripts/ci/live-eval.mjs (read that file first, this one mirrors its
 * structure closely — this script's own job is much smaller: it just
 * forwards flags to the already-fully-featured .NET CLI
 * (`Comuki.AgentEval.Program`), which does its own corpus loading,
 * real-pi driving, judging, scoring, report writing, and history
 * appending. This wrapper exists so a human has one short, memorable,
 * dependency-free command and one place to see the "manual only" /
 * "no paid API from CI" guarantees documented.
 *
 * Usage:
 *   node scripts/ci/agent-eval.mjs --mode=fake --budget-usd=0
 *   node scripts/ci/agent-eval.mjs --mode=fake --corpus=tests/fixtures/scenarios/agent-eval --budget-usd=0
 *   node scripts/ci/agent-eval.mjs --mode=live --budget-usd=5.00     # requires COMUKI_LIVE_MODEL_BASE_URL
 *   node scripts/ci/agent-eval.mjs --trend                            # prints the quality/cost history table, no run
 *
 * CRITICAL: this script never reads a paid-API credential from anywhere
 * other than the calling environment, and never fabricates one.
 * --mode=live without COMUKI_LIVE_MODEL_BASE_URL set prints a skip
 * message and exits 0 — never a failure, matching the .NET side's own
 * behavior (Comuki.AgentEval.Program.Main). This script only ever
 * shells out to `dotnet run --project tests/tools/Comuki.AgentEval` —
 * it never calls a model/judge API directly itself.
 */
import { spawnSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Absolute path to the eval-harness csproj this script drives. */
const DEFAULT_PROJECT_PATH = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '..', '..', 'tests', 'tools', 'Comuki.AgentEval', 'Comuki.AgentEval.csproj',
);

/** Env var this script reads (never writes) — the live upstream's base URL. Absence means "skip" for --mode=live only. */
const LIVE_BASE_URL_ENV_VAR = 'COMUKI_LIVE_MODEL_BASE_URL';

const USAGE = `agent-eval — WS10 MANUAL entry point for the golden-task eval harness (Comuki.AgentEval)

Usage:
  node scripts/ci/agent-eval.mjs --mode=fake|replay|live [--corpus=<dir>] [--budget-usd=<n>]
  node scripts/ci/agent-eval.mjs --trend
  node scripts/ci/agent-eval.mjs --help

Arguments:
  --mode=<m>         fake | replay | live — required unless --trend is the only flag.
  --corpus=<dir>     repo-relative or absolute corpus directory. Defaults to the
                      .NET CLI's own default (tests/fixtures/scenarios/agent-eval).
  --budget-usd=<n>   process-wide USD ceiling. Defaults to 0 (= unlimited; combines
                      with COMUKI_LIVE_BUDGET_MAX_USD via smaller-of-two-wins).
  --trend            prints the quality/cost history table
                      (artifacts/agent-eval/history.jsonl) and exits — no eval run.
                      Only takes effect when passed WITHOUT --mode (matches the
                      .NET CLI's own --trend gate).

Environment (read from the calling shell, never written by this script):
  COMUKI_LIVE_MODEL_BASE_URL   required to activate --mode=live; absent = a clean
                                skip message, exit 0. Never read for fake/replay.
  COMUKI_LIVE_MODEL_TOKEN      optional bearer/API token for the live upstream.
  COMUKI_LIVE_BUDGET_MAX_USD   alternative global budget ceiling (smaller-of-two-wins
                                with --budget-usd, same rule the .NET runner enforces).

Notes:
  - MANUAL ENTRY POINT ONLY. Never invoked from .github/workflows/ci.yml or
    deploy/hybrid/ci.yml — CI wiring is a separate, out-of-scope workstream (WS12).
  - This script only ever shells out to 'dotnet run --project tests/tools/Comuki.AgentEval'.
    It never calls a paid API directly itself.
  - --mode=live drives a REAL model call whenever COMUKI_LIVE_MODEL_BASE_URL is set —
    never invoke it pointed at a paid API unless you mean to spend the budget you passed.
`;

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

/**
 * Parses CLI arguments. Returns one of:
 *   - { kind: 'usage', exitCode, message }
 *   - { kind: 'args', options: {mode: string|null, corpusPath: string|null, budgetUsd: string, trend: boolean} }
 *   - { kind: 'error', message }
 *
 * @param {string[]} argv
 */
export function parseArgs(argv) {
  if (!Array.isArray(argv) || argv.length === 0) {
    return { kind: 'usage', exitCode: 1, message: USAGE };
  }

  if (argv.includes('--help') || argv.includes('-h')) {
    return { kind: 'usage', exitCode: 0, message: USAGE };
  }

  const valueOf = (prefix) => {
    const hit = argv.find((arg) => arg.startsWith(prefix));
    return hit === undefined ? null : hit.slice(prefix.length);
  };

  const mode = valueOf('--mode=');
  const corpusPath = valueOf('--corpus=');
  const budgetRaw = valueOf('--budget-usd=');
  const trend = argv.includes('--trend');

  if (mode === null && !trend) {
    return { kind: 'error', message: '--mode=<fake|replay|live> is required (or pass --trend alone).' };
  }

  if (mode !== null && !['fake', 'replay', 'live'].includes(mode)) {
    return { kind: 'error', message: `--mode='${mode}' is not one of fake|replay|live` };
  }

  let budgetUsd = '0';
  if (budgetRaw !== null) {
    const parsed = Number(budgetRaw);
    if (!Number.isFinite(parsed) || parsed < 0) {
      return { kind: 'error', message: `--budget-usd must be a non-negative number, got '${budgetRaw}'` };
    }
    budgetUsd = budgetRaw;
  }

  return {
    kind: 'args',
    options: { mode, corpusPath, budgetUsd, trend },
  };
}

/**
 * Resolves whether the run should proceed. `--trend`-only and
 * `--mode=fake`/`--mode=replay` always proceed; `--mode=live` gates on
 * {@link LIVE_BASE_URL_ENV_VAR} being set in the SUPPLIED env object
 * (never the script's own ambient `process.env` directly — keeps this
 * function pure and testable).
 *
 * @param {NodeJS.ProcessEnv} env
 * @param {string|null} mode
 * @returns {{shouldRun: true} | {shouldRun: false, reason: string}}
 */
export function resolveActivation(env, mode) {
  if (mode !== 'live') {
    return { shouldRun: true };
  }

  const baseUrl = env?.[LIVE_BASE_URL_ENV_VAR];
  if (baseUrl === undefined || baseUrl.trim() === '') {
    return {
      shouldRun: false,
      reason: `${LIVE_BASE_URL_ENV_VAR} is not set — --mode=live skipped (this is expected in CI; `
        + 'set it to a scripted FakeModelServer or a real gateway URL to run a live eval locally).',
    };
  }

  return { shouldRun: true };
}

/**
 * Drives the .NET side: spawns `dotnet run` against the eval-harness
 * project, forwarding exactly the flags the caller resolved. The .NET
 * CLI (`Comuki.AgentEval.Program`) owns everything past this point —
 * corpus loading, real-pi driving, judging, scoring, report writing,
 * and history appending.
 *
 * `runDotnet` defaults to {@link defaultRunDotnet}; tests pass a fake.
 *
 * @param {{mode: string|null, corpusPath: string|null, budgetUsd: string, trend: boolean, projectPath: string, env: NodeJS.ProcessEnv, runDotnet?: RunDotnet}} input
 * @returns {RunAgentEvalResult}
 */
export function runAgentEval(input) {
  const runDotnet = input.runDotnet ?? defaultRunDotnet;

  const args = ['run', '--project', input.projectPath, '--no-build', '--'];
  if (input.mode !== null) {
    args.push(`--mode=${input.mode}`);
  }
  if (input.corpusPath !== null) {
    args.push(`--corpus=${input.corpusPath}`);
  }
  args.push(`--budget-usd=${input.budgetUsd}`);
  if (input.trend) {
    args.push('--trend');
  }

  return runDotnet(args, input.env);
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
 * }} RunAgentEvalResult
 *
 * @typedef {(args: string[], env: NodeJS.ProcessEnv) => RunAgentEvalResult} RunDotnet
 */

/**
 * Top-level CLI entrypoint. Returns a process exit code; never calls
 * `process.exit` itself so callers (tests) can drive it without side
 * effects.
 *
 * @param {string[]} argv
 * @param {{runDotnet?: RunDotnet, stdout?: NodeJS.WritableStream, stderr?: NodeJS.WritableStream, projectPath?: string, env?: NodeJS.ProcessEnv}} [deps]
 * @returns {number}
 */
export function main(argv, deps = {}) {
  const stdout = deps.stdout ?? process.stdout;
  const stderr = deps.stderr ?? process.stderr;
  const runDotnet = deps.runDotnet;
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

  const { mode, corpusPath, budgetUsd, trend } = parsed.options;

  const activation = resolveActivation(env, mode);
  if (!activation.shouldRun) {
    stdout.write(`agent-eval: skipped — ${activation.reason}\n`);
    return 0;
  }

  if (mode !== null) {
    stdout.write(`agent-eval: mode = ${mode}\n`);
    stdout.write(`agent-eval: corpus = ${corpusPath ?? '(default: tests/fixtures/scenarios/agent-eval)'}\n`);
    stdout.write(`agent-eval: budget = $${budgetUsd}\n`);
  }
  else {
    stdout.write('agent-eval: --trend only — reading history, no run\n');
  }

  const result = runAgentEval({ mode, corpusPath, budgetUsd, trend, projectPath, env, runDotnet });

  if (result.error !== null) {
    stderr.write(`agent-eval: failed to spawn dotnet: ${result.error.message}\n`);
    return 1;
  }

  if (result.stdout.length > 0) {
    stdout.write(result.stdout);
  }

  if (result.status !== 0) {
    stderr.write(`agent-eval: dotnet run exited with status ${result.status}\n`);
    if (result.stderr.length > 0) {
      stderr.write(`--- dotnet stderr (tail) ---\n${tailLines(result.stderr, 50)}\n`);
    }
    return 1;
  }

  return 0;
}

function tailLines(text, lines) {
  const all = text.split(/\r?\n/);
  if (all.length <= lines) {
    return all.join('\n');
  }
  return all.slice(-lines).join('\n');
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  process.exitCode = main(process.argv.slice(2));
}
