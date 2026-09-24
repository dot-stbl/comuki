#!/usr/bin/env node
/**
 * record-cassette — WS8 re-record command for the agent-loop cassette
 * corpus. Drives a real-pi translator cycle through a recording
 * CassetteModelServer pointed at a caller-provided upstream URL (a paid
 * Anthropic endpoint, a staging proxy, or a throwaway FakeModelServer
 * spun up just for the run), and lands a fresh cassette at the path the
 * scenario declares — human reviews `git diff` after; the script never
 * auto-commits (design.md "no auto-commit").
 *
 * Pure, exported functions (`parseArgs`, `extractModelCassette`,
 * `resolveCassettePath`, `preflight`, `runRecord`) plus a small CLI:
 *
 *   node scripts/ci/record-cassette.mjs --scenario tests/fixtures/scenarios/small-repo/add-null-check-replay.scenario.yaml --upstream http://127.0.0.1:17190
 *   node scripts/ci/record-cassette.mjs --scenario <f> --upstream <url> --cassette-out /tmp/foo.json --force
 *   node scripts/ci/record-cassette.mjs --budget-usd 5.00 --scenario <f> --upstream <url>
 *
 * Zero npm dependencies on purpose: the script has to work on a bare
 * clone with nothing installed but node.
 *
 * Env vars set on the child dotnet process (consumed by tests/integration/
 * Comuki.EndToEnd.AgentLoop/RealPi/RecordCassetteShould.cs):
 *   COMUKI_RECORD_SCENARIO        (required) absolute path to the scenario YAML
 *   COMUKI_RECORD_UPSTREAM        (required) absolute URL of the upstream
 *   COMUKI_RECORD_CASSETTE_OUT    (optional) absolute cassette output path;
 *                                          defaults to scenario's model.cassette
 *   COMUKI_RECORD_BUDGET_USD      (optional) decimal string echoed back by
 *                                          the .NET side (WS9 wires enforcement)
 */
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, isAbsolute, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Absolute path to the integration test csproj this script drives. */
const DEFAULT_PROJECT_PATH = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '..', '..', 'tests', 'integration', 'Comuki.EndToEnd.AgentLoop', 'Comuki.EndToEnd.AgentLoop.csproj',
);

const USAGE = `record-cassette — WS8 re-record command for agent-loop cassettes

Usage:
  node scripts/ci/record-cassette.mjs --scenario <file> --upstream <url> [--cassette-out <path>] [--budget-usd <n>] [--force]
  node scripts/ci/record-cassette.mjs --help

Arguments:
  --scenario <file>     absolute path to the .scenario.yaml file to re-record (required)
  --upstream <url>      absolute URL of the upstream the recording proxy forwards to (required)
  --cassette-out <path> absolute path to write the cassette to; defaults to scenario's model.cassette
  --budget-usd <n>      optional decimal budget (plumbing only — WS9 wires enforcement)
  --force               overwrite the existing cassette file if it already exists

Environment:
  The script sets COMUKI_RECORD_SCENARIO, COMUKI_RECORD_UPSTREAM,
  COMUKI_RECORD_CASSETTE_OUT, COMUKI_RECORD_BUDGET_USD on the spawned
  'dotnet run' process. The .NET side (RecordCassetteShould) is a no-op
  unless both COMUKI_RECORD_SCENARIO and COMUKI_RECORD_UPSTREAM are set.

Notes:
  - The script never auto-commits the recorded cassette. A human reviews
    'git diff' on the cassette file afterward (design.md "no auto-commit").
  - The script never touches the existing committed cassette at its
    declared path unless --force is passed.
  - The .NET side's record path is itself a real-pi translator cycle, so
    it needs the same Podman/Postgres/pi-binary stack the WS7 fact does.
`;

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

/**
 * Extracts the value of `model.cassette:` (or `model:\n  cassette:`) from a
 * scenario YAML's text. Returns the scalar value as a string, or null if no
 * `model:` block was found at all / no `cassette:` key lives under it.
 *
 * Pure dependency-free YAML scanner — matches the same shape the .NET-side
 * `ScenarioLoader` accepts (2-space indent under top-level `model:`, no
 * folded scalars in that field). Handles quoted and unquoted values.
 *
 * @param {string} scenarioText
 * @returns {string | null}
 */
export function extractModelCassette(scenarioText) {
  if (typeof scenarioText !== 'string') {
    return null;
  }

  const lines = scenarioText.split(/\r\n|\r|\n/);
  let inModel = false;
  let modelIndent = -1;

  for (const raw of lines) {
    const line = raw.replace(/\s+$/, '');
    const trimmed = line.trim();

    // Skip blank lines and YAML comments everywhere — neither can carry
    // a section header or a key.
    if (trimmed === '' || trimmed.startsWith('#')) {
      continue;
    }

    const leading = line.length - line.trimStart().length;

    if (!inModel) {
      // Top-level header — leading whitespace == 0.
      if (leading === 0 && /^model\s*:\s*(?:#.*)?$/.test(trimmed)) {
        inModel = true;
        // Children of this block live at leading > 0; we the indent off the
        // next non-blank line we see.
        continue;
      }

      continue;
    }

    // We're inside `model:`. A line back at leading == 0 closes the block.
    if (leading === 0) {
      inModel = false;
      continue;
    }

    if (modelIndent < 0) {
      modelIndent = leading;
    }
    else if (leading < modelIndent) {
      // We've stepped out of the model's child block.
      inModel = false;
      continue;
    }

    const match = /^cassette\s*:\s*(.*)$/.exec(trimmed);
    if (match !== null) {
      return unquoteYamlScalar(match[1].trim());
    }
  }

  return null;
}

/** Strip a single layer of matched `"..."` or `'...'` quoting from a YAML scalar. */
function unquoteYamlScalar(value) {
  if (value.length >= 2) {
    const head = value[0];
    const tail = value[value.length - 1];
    if ((head === '"' && tail === '"') || (head === "'" && tail === "'")) {
      return value.slice(1, -1);
    }
  }

  return value;
}

/**
 * Resolves a scenario-relative path the way `ScenarioLoader.ResolveRelativeToScenario`
 * does on the .NET side: take the scenario file's own directory and combine
 * it with the relative path, then absolutize.
 *
 * @param {string} scenarioPath
 * @param {string} relativePath
 * @returns {string}
 */
export function resolveCassettePath(scenarioPath, relativePath) {
  if (!isAbsolute(scenarioPath)) {
    throw new Error(`scenario path must be absolute: ${scenarioPath}`);
  }

  if (!isAbsolute(relativePath)) {
    return resolve(dirname(scenarioPath), relativePath);
  }

  return relativePath;
}

/**
 * Parses CLI arguments. Returns one of:
 *   - { kind: 'usage', exitCode, message } — caller should print + return
 *   - { kind: 'args', options } — proceed
 *   - { kind: 'error', message } — caller should print + return exitCode 1
 *
 * @param {string[]} argv
 * @returns {{kind: 'usage', exitCode: number, message: string} | {kind: 'args', options: ParsedOptions} | {kind: 'error', message: string}}
 */
export function parseArgs(argv) {
  if (!Array.isArray(argv) || argv.length === 0) {
    return { kind: 'usage', exitCode: 1, message: USAGE };
  }

  if (argv.includes('--help') || argv.includes('-h')) {
    return { kind: 'usage', exitCode: 0, message: USAGE };
  }

  const valueOf = (flag) => {
    const index = argv.indexOf(flag);
    if (index === -1) {
      return null;
    }
    const next = argv[index + 1];
    if (next === undefined || next.startsWith('--')) {
      return null;
    }
    return next;
  };

  const scenario = valueOf('--scenario');
  const upstream = valueOf('--upstream');
  const cassetteOut = valueOf('--cassette-out');
  const budgetRaw = valueOf('--budget-usd');
  const force = argv.includes('--force');

  const errors = [];
  if (scenario === null || scenario.trim() === '') {
    errors.push('--scenario is required (absolute path to a .scenario.yaml file)');
  }
  if (upstream === null || upstream.trim() === '') {
    errors.push('--upstream is required (absolute URL the recording proxy forwards to)');
  }

  let budgetUsd = null;
  if (budgetRaw !== null) {
    const parsed = Number(budgetRaw);
    if (!Number.isFinite(parsed) || parsed < 0) {
      errors.push(`--budget-usd must be a non-negative number, got '${budgetRaw}'`);
    }
    else {
      budgetUsd = budgetRaw;
    }
  }

  if (errors.length > 0) {
    return { kind: 'error', message: errors.join('\n') };
  }

  return {
    kind: 'args',
    options: {
      scenarioPath: scenario,
      upstreamUrl: upstream,
      cassetteOutPath: cassetteOut,
      budgetUsd,
      force,
    },
  };
}

/**
 * Pre-flight: refuses to proceed if the resolved cassette path already
 * exists on disk and `--force` was not passed. Returns the verdict; the
 * caller decides what to print.
 *
 * @param {{cassettePath: string, force: boolean}} input
 * @returns {{ok: true} | {ok: false, reason: string}}
 */
export function preflight({ cassettePath, force }) {
  if (existsSync(cassettePath) && !force) {
    return {
      ok: false,
      reason: `cassette file already exists at '${cassettePath}' — pass --force to overwrite`,
    };
  }

  return { ok: true };
}

/**
 * Drives the .NET side: spawns `dotnet run` against the integration test
 * project with the four COMUKI_RECORD_* env vars set on the child process.
 *
 * `runDotnet` defaults to {@link defaultRunDotnet} (the real
 * `child_process.spawnSync` against `dotnet`); tests pass a fake.
 *
 * @param {RunRecordInput & {runDotnet?: RunDotnet}} input
 * @returns {RunRecordResult}
 */
export function runRecord(input) {
  const runDotnet = input.runDotnet ?? defaultRunDotnet;

  const env = {
    ...input.env,
    COMUKI_RECORD_SCENARIO: input.scenarioPath,
    COMUKI_RECORD_UPSTREAM: input.upstreamUrl,
  };
  if (input.cassetteOutPath !== null) {
    env.COMUKI_RECORD_CASSETTE_OUT = input.cassetteOutPath;
  }
  if (input.budgetUsd !== null) {
    env.COMUKI_RECORD_BUDGET_USD = input.budgetUsd;
  }

  const args = ['run', '--project', input.projectPath, '--no-build'];
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
 * Verifies the cassette file the .NET run was supposed to produce. Checks
 * existence and that the JSON parses with a top-level `exchanges` array
 * of at least one entry — the same shape `CassetteReader.LoadFromFile` /
 * `CassetteWriter.AppendExchangeAsync` round-trip on the .NET side.
 *
 * @param {string} cassettePath
 * @returns {{ok: true, exchangeCount: number} | {ok: false, reason: string}}
 */
export function verifyCassette(cassettePath) {
  if (!existsSync(cassettePath)) {
    return { ok: false, reason: `cassette file does not exist at '${cassettePath}' after the .NET run` };
  }

  let parsed;
  try {
    parsed = JSON.parse(readFileSync(cassettePath, 'utf8'));
  }
  catch (error) {
    return { ok: false, reason: `cassette at '${cassettePath}' is not valid JSON: ${error.message}` };
  }

  if (!Array.isArray(parsed?.exchanges) || parsed.exchanges.length === 0) {
    return { ok: false, reason: `cassette at '${cassettePath}' has no top-level 'exchanges' array with at least one entry` };
  }

  return { ok: true, exchangeCount: parsed.exchanges.length };
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

/**
 * @typedef {{
 *   scenarioPath: string,
 *   upstreamUrl: string,
 *   cassetteOutPath: string | null,
 *   budgetUsd: string | null,
 *   force: boolean,
 * }} ProjectOptions
 *
 * @typedef {{
 *   scenarioPath: string,
 *   upstreamUrl: string,
 *   cassetteOutPath: string | null,
 *   budgetUsd: string | null,
 *   projectPath: string,
 *   env: NodeJS.ProcessEnv,
 *   runDotnet?: RunDotnet,
 * }} RunRecordInput
 *
 * @typedef {{
 *   status: number | null,
 *   signal: NodeJS.Signals | null,
 *   stdout: string,
 *   stderr: string,
 *   error: Error | null,
 * }} RunRecordResult
 *
 * @typedef {(args: string[], env: NodeJS.ProcessEnv) => RunRecordResult} RunDotnet
 */

/**
 * Resolves the cassette output path: `--cassette-out` if provided,
 * otherwise the scenario's own `model.cassette:` resolved relative to the
 * scenario file's directory. Returns null if neither resolves cleanly —
 * the caller surfaces the error to the human.
 *
 * @param {{scenarioPath: string, cassetteOutPath: string | null, scenarioText: string}} input
 * @returns {string | null}
 */
export function resolveOutputPath({ scenarioPath, cassetteOutPath, scenarioText }) {
  if (cassetteOutPath !== null) {
    return resolve(cassetteOutPath);
  }

  const relative = extractModelCassette(scenarioText);
  if (relative === null) {
    return null;
  }

  return resolveCassettePath(scenarioPath, relative);
}

/**
 * Top-level CLI entrypoint. Returns a process exit code; never calls
 * `process.exit` itself so callers (tests) can drive it without side
 * effects.
 *
 * @param {string[]} argv
 * @param {{runDotnet?: RunDotnet, readFileSync?: typeof readFileSync, stdout?: NodeJS.WritableStream, stderr?: NodeJS.WritableStream, projectPath?: string}} [deps]
 * @returns {number}
 */
export function main(argv, deps = {}) {
  const stdout = deps.stdout ?? process.stdout;
  const stderr = deps.stderr ?? process.stderr;
  const runDotnet = deps.runDotnet;
  const reader = deps.readFileSync ?? readFileSync;
  const projectPath = deps.projectPath ?? DEFAULT_PROJECT_PATH;

  const parsed = parseArgs(argv);
  if (parsed.kind === 'usage') {
    stdout.write(parsed.message);
    return parsed.exitCode;
  }
  if (parsed.kind === 'error') {
    stderr.write(`${parsed.message}\n\n${USAGE}`);
    return 1;
  }

  const options = parsed.options;

  let scenarioText;
  try {
    scenarioText = reader(options.scenarioPath, 'utf8');
  }
  catch (error) {
    stderr.write(`could not read scenario file '${options.scenarioPath}': ${error.message}\n`);
    return 1;
  }

  const cassettePath = resolveOutputPath({
    scenarioPath: options.scenarioPath,
    cassetteOutPath: options.cassetteOutPath,
    scenarioText,
  });
  if (cassettePath === null) {
    stderr.write(
      `could not resolve a cassette output path: --cassette-out was not passed and scenario '${options.scenarioPath}' declares no model.cassette:\n`
        + `  (model.cassette: scalar not found under a top-level model: block in the file)\n`,
    );
    return 1;
  }

  const guard = preflight({ cassettePath, force: options.force });
  if (!guard.ok) {
    stderr.write(`${guard.reason}\n`);
    return 1;
  }

  if (options.budgetUsd !== null) {
    stdout.write(`record-cassette: budget requested = $${options.budgetUsd} (plumbing only — WS9 wires enforcement)\n`);
  }
  stdout.write(`record-cassette: recording scenario ${options.scenarioPath}\n`);
  stdout.write(`record-cassette: upstream = ${options.upstreamUrl}\n`);
  stdout.write(`record-cassette: cassette-out = ${cassettePath}\n`);

  const result = runRecord({
    scenarioPath: options.scenarioPath,
    upstreamUrl: options.upstreamUrl,
    cassetteOutPath: options.cassetteOutPath,
    budgetUsd: options.budgetUsd,
    projectPath,
    env: process.env,
    runDotnet,
  });

  if (result.error !== null) {
    stderr.write(`record-cassette: failed to spawn dotnet: ${result.error.message}\n`);
    return 1;
  }

  if (result.status !== 0) {
    stderr.write(`record-cassette: dotnet run exited with status ${result.status}\n`);
    if (result.stdout.length > 0) {
      stderr.write(`--- dotnet stdout (tail) ---\n${tailLines(result.stdout, 50)}\n`);
    }
    if (result.stderr.length > 0) {
      stderr.write(`--- dotnet stderr (tail) ---\n${tailLines(result.stderr, 50)}\n`);
    }
    return 1;
  }

  const verdict = verifyCassette(cassettePath);
  if (!verdict.ok) {
    stderr.write(`record-cassette: post-run verification failed — ${verdict.reason}\n`);
    return 1;
  }

  stdout.write(`record-cassette: ok — wrote ${verdict.exchangeCount} exchange(s) to ${cassettePath}\n`);
  stdout.write(`record-cassette: review 'git diff' on the cassette file and commit when ready.\n`);
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