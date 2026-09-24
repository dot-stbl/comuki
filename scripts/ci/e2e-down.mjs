#!/usr/bin/env node
/**
 * e2e-down — WS13 stack tear-down (openspec add-agentic-test-contour).
 *
 * Runs `podman compose -f deploy/compose.e2e.yml down -v --remove-orphans`
 * to stop every container, drop the named volumes (postgres data,
 * minio data) so a fresh `up` starts from a clean schema, and remove
 * orphans. Also best-effort removes artifacts/e2e/bootstrap.json so the
 * next up writes a fresh one.
 *
 * Usage:
 *   node scripts/ci/e2e-down.mjs
 *   node scripts/ci/e2e-down.mjs --compose-cmd=docker
 *   node scripts/ci/e2e-down.mjs --timeout=180000
 *   node scripts/ci/e2e-down.mjs --keep-bootstrap        # don't delete bootstrap.json
 *   node scripts/ci/e2e-down.mjs --help
 *
 * Zero npm dependencies — bun/node builtins only. Same shape as
 * scripts/ci/e2e-up.mjs (exported pure helpers + injectable deps +
 * main() returning an exit code).
 */
import { spawnSync } from 'node:child_process';
import { existsSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const REPO_ROOT = path.resolve(path.dirname(__filename), '..', '..');

const COMPOSE_FILE = path.join(REPO_ROOT, 'deploy', 'compose.e2e.yml');
const BOOTSTRAP_PATH = path.join(REPO_ROOT, 'artifacts', 'e2e', 'bootstrap.json');

const DEFAULT_TIMEOUT_MS = 120_000;        // 2 min — down is fast

const USAGE = `e2e-down — WS13 hermetic compose stack tear-down

Usage:
  node scripts/ci/e2e-down.mjs [--compose-cmd=<podman|docker>]
                                [--timeout=<ms>] [--keep-bootstrap]
  node scripts/ci/e2e-down.mjs --help

Options:
  --compose-cmd=<cmd>    Override compose binary auto-detection.
                         'podman' tries 'podman compose version',
                         'docker' tries 'docker compose version'.
                         Default: auto (prefer podman, fall back to docker).
  --timeout=<ms>          Hard deadline for the whole tear-down sequence.
                         Default: 120000 (2 min).
  --keep-bootstrap        Don't delete artifacts/e2e/bootstrap.json. Useful
                         when you intend to keep poking at the same stack
                         from the smoke script without re-running login.

Notes:
  - Always run this in a finally/trap around e2e-up.mjs / e2e-smoke.mjs.
    e2e-up intentionally does NOT auto-teardown on failure so a post-
    mortem can read the live stack; the caller owns clean-up.
  - 'down -v' drops the postgres + minio volumes. Next 'up' starts with
    an empty database, so tests start deterministic. Use a separate
    invocation without '-v' if you want to keep data — not currently
    supported, the WS13 contract is "down = fully fresh".
`;

// ---------------------------------------------------------------------------
// Arg parsing
// ---------------------------------------------------------------------------

/**
 * @param {string[]} argv
 * @returns {{ ok: true, options: object } | { ok: false, error: string }}
 */
export function parseArgs(argv) {
  const options = {
    composeCmd: 'auto',
    timeoutMs: DEFAULT_TIMEOUT_MS,
    keepBootstrap: false,
    help: false,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    if (raw === '--help' || raw === '-h') {
      options.help = true;
      continue;
    }
    if (raw === '--keep-bootstrap') {
      options.keepBootstrap = true;
      continue;
    }
    const eq = raw.indexOf('=');
    const flag = eq === -1 ? raw : raw.slice(0, eq);
    const inlineValue = eq === -1 ? undefined : raw.slice(eq + 1);
    const takeValue = () => {
      if (inlineValue !== undefined) {
        return inlineValue;
      }
      const next = argv[i + 1];
      if (next === undefined || next.startsWith('--')) {
        return undefined;
      }
      i += 1;
      return next;
    };
    const v = takeValue();
    if (flag === '--compose-cmd') {
      if (v === undefined || !['auto', 'podman', 'docker'].includes(v)) {
        return { ok: false, error: '--compose-cmd must be auto|podman|docker' };
      }
      options.composeCmd = v;
      continue;
    }
    if (flag === '--timeout') {
      if (v === undefined || !/^\d+$/.test(v)) {
        return { ok: false, error: '--timeout must be a positive integer (ms)' };
      }
      options.timeoutMs = Number(v);
      continue;
    }
    return { ok: false, error: `unrecognized argument: ${raw}` };
  }

  return { ok: true, options };
}

// ---------------------------------------------------------------------------
// Compose down
// ---------------------------------------------------------------------------

/**
 * Run `<cmd> compose -f deploy/compose.e2e.yml down -v --remove-orphans`.
 * @param {{ cmd: string, run?: typeof spawnSync, file?: string, env?: NodeJS.ProcessEnv, cwd?: string }} input
 */
export function composeDown({
  cmd,
  run = spawnSync,
  file = COMPOSE_FILE,
  env = process.env,
  cwd = REPO_ROOT,
}) {
  const args = ['compose', '-f', file, 'down', '-v', '--remove-orphans'];
  return run(cmd, args, {
    cwd,
    env,
    stdio: ['ignore', 'inherit', 'inherit'],
    encoding: 'utf8',
  });
}

// ---------------------------------------------------------------------------
// Compose-cmd detection — duplicate of e2e-up's so the file is self-contained
// ---------------------------------------------------------------------------

/**
 * @param {{ run: typeof spawnSync, env?: NodeJS.ProcessEnv }} [deps]
 * @returns {string}
 */
export function detectComposeCmd({ run = spawnSync, env = process.env } = {}) {
  const probe = (cmd) => {
    const result = run(cmd, ['compose', 'version'], {
      env,
      stdio: ['ignore', 'pipe', 'pipe'],
      encoding: 'utf8',
    });
    if (result.error) {
      return false;
    }
    return result.status === 0;
  };
  if (probe('podman')) {
    return 'podman';
  }
  if (probe('docker')) {
    return 'docker';
  }
  throw new Error(
    'no compose-capable runtime found (tried `podman compose version` and `docker compose version`; both failed). ' +
      'Install Docker Desktop / Podman + the docker compose plugin, or point --compose-cmd explicitly at the one you have.',
  );
}

// ---------------------------------------------------------------------------
// Bootstrap.json removal
// ---------------------------------------------------------------------------

/**
 * Best-effort: removes the file if it exists, swallows any error (a leftover
 * bootstrap.json is not worth failing the command over). Returns whether a
 * file was actually removed.
 *
 * @param {string} path
 * @returns {boolean}
 */
export function removeBootstrapFile(filePath = BOOTSTRAP_PATH) {
  if (!existsSync(filePath)) {
    return false;
  }
  try {
    rmSync(filePath, { force: true });
    return true;
  } catch {
    return false;
  }
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

/**
 * @param {string[]} argv
 * @param {{
 *   run?: typeof spawnSync,
 *   stdout?: NodeJS.WritableStream,
 *   stderr?: NodeJS.WritableStream,
 *   env?: NodeJS.ProcessEnv,
 *   cwd?: string,
 *   repoRoot?: string,
 *   composeDownFn?: typeof composeDown,
 *   detectComposeCmdFn?: typeof detectComposeCmd,
 *   removeBootstrapFn?: typeof removeBootstrapFile,
 * }} [deps]
 */
export function main(argv, deps = {}) {
  const stdout = deps.stdout ?? process.stdout;
  const stderr = deps.stderr ?? process.stderr;
  const run = deps.run ?? spawnSync;
  const env = deps.env ?? process.env;
  const cwd = deps.cwd ?? REPO_ROOT;

  const parsed = parseArgs(argv);
  if (!parsed.ok) {
    stderr.write(`${parsed.error}\n\n${USAGE}`);
    return 1;
  }
  const { options } = parsed;
  if (options.help) {
    stdout.write(USAGE);
    return 0;
  }

  const composeCmd = options.composeCmd === 'auto'
    ? (deps.detectComposeCmdFn ?? detectComposeCmd)({ run, env })
    : options.composeCmd;
  stdout.write(`e2e-down: compose-cmd = ${composeCmd}\n`);

  const downResult = (deps.composeDownFn ?? composeDown)({ cmd: composeCmd, run, env, cwd });
  if (downResult.error !== null && downResult.error !== undefined) {
    stderr.write(`e2e-down: failed to spawn compose down: ${downResult.error.message}\n`);
    return 1;
  }
  if (downResult.status !== 0) {
    stderr.write(`e2e-down: ${composeCmd} compose down exited ${downResult.status}\n`);
    return downResult.status ?? 1;
  }

  if (!options.keepBootstrap) {
    const removed = (deps.removeBootstrapFn ?? removeBootstrapFile)();
    if (removed) {
      stdout.write(`e2e-down: removed ${path.relative(REPO_ROOT, BOOTSTRAP_PATH)}\n`);
    }
  }

  stdout.write('e2e-down: stack torn down\n');
  return 0;
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  process.exitCode = main(process.argv.slice(2));
}