#!/usr/bin/env node
/**
 * e2e-up — WS13 stack boot (openspec add-agentic-test-contour).
 *
 * Builds the TestFakeModel image (raw `podman build --ignorefile` — compose
 * has no YAML key to pass a custom ignorefile, see the file header on
 * tests/tools/Comuki.TestFakeModel/Dockerfile), brings up the T3 hermetic
 * compose stack declared in deploy/compose.e2e.yml, then logs in as the
 * bootstrap admin and stamps an API key. Writes
 * artifacts/e2e/bootstrap.json = { baseUrl, apiKey, userId, keyId,
 * createdAt } so e2e-smoke.mjs (or a human) can drive the stack without
 * repeating the login dance.
 *
 * The caller is responsible for always invoking e2e-down.mjs in a
 * finally/trap — e2e-up does NOT auto-teardown on failure (it logs every
 * service's tail and exits 1 so a post-mortem can read the live stack).
 *
 * Usage:
 *   node scripts/ci/e2e-up.mjs
 *   node scripts/ci/e2e-up.mjs --skip-build            # skip all image builds
 *   node scripts/ci/e2e-up.mjs --compose-cmd=docker     # force docker compose
 *   node scripts/ci/e2e-up.mjs --timeout=600000         # 10-min compose up deadline
 *   node scripts/ci/e2e-up.mjs --help
 *
 * Zero npm dependencies — bun/node builtins only. Same shape as
 * scripts/ci/live-eval.mjs (exported pure helpers + injectable deps +
 * main() returning an exit code).
 */
import { spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const REPO_ROOT = path.resolve(path.dirname(__filename), '..', '..');

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const COMPOSE_FILE = path.join(REPO_ROOT, 'deploy', 'compose.e2e.yml');

const TEST_FAKE_MODEL_IMAGE = 'comuki-test-fake-model:e2e';
const TEST_FAKE_MODEL_DOCKERFILE = path.join(
  REPO_ROOT,
  'tests',
  'tools',
  'Comuki.TestFakeModel',
  'Dockerfile',
);
const TEST_FAKE_MODEL_IGNORE = path.join(
  REPO_ROOT,
  'tests',
  'tools',
  'Comuki.TestFakeModel',
  '.containerignore',
);

const BOOTSTRAP_DIR = path.join(REPO_ROOT, 'artifacts', 'e2e');
const BOOTSTRAP_PATH = path.join(BOOTSTRAP_DIR, 'bootstrap.json');

const DEFAULT_TIMEOUT_MS = 900_000;        // 15 min for compose up + healthchecks
const COMPOSE_UP_WAIT_TIMEOUT = 600;       // seconds; passed as --wait-timeout
const DEFAULT_BASE_URL = 'http://localhost:17180';

const USAGE = `e2e-up — WS13 hermetic compose stack boot

Usage:
  node scripts/ci/e2e-up.mjs [--skip-build] [--compose-cmd=<podman|docker>]
                              [--timeout=<ms>] [--base-url=<url>]
                              [--admin-email=<e>] [--admin-password=<p>]
  node scripts/ci/e2e-up.mjs --help

Options:
  --skip-build           Skip all image builds (assumes compose images and
                         comuki-test-fake-model:e2e already exist).
  --compose-cmd=<cmd>    Override compose binary auto-detection.
                         'podman' tries 'podman compose version',
                         'docker' tries 'docker compose version'.
                         Default: auto (prefer podman, fall back to docker).
  --timeout=<ms>         Hard deadline for the whole boot sequence
                         (builds + compose up + login + key stamp).
                         Default: 900000 (15 min).
  --base-url=<url>       The host's published URL the smoke script should
                         target. Default: http://localhost:17180 — must
                         match COMUKI_PUBLIC_HOST_URL inside the host
                         container (see deploy/compose.e2e.yml).
  --admin-email=<e>      Bootstrap admin email. Default: e2e-admin@comuki.local.
  --admin-password=<p>   Bootstrap admin password. Default: comuki-e2e-dev-only.

Notes:
  - ALWAYS run e2e-down.mjs in a finally/trap at the call site. e2e-up does
    NOT auto-teardown on failure — it leaves the stack up for post-mortem
    and prints every service's tail before exiting 1.
  - The baseUrl written into artifacts/e2e/bootstrap.json is the value
    passed here (or the default), NOT a value read from the stack — keep
    them consistent with --compose-cmd's port mappings.
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
    skipBuild: false,
    composeCmd: 'auto',
    timeoutMs: DEFAULT_TIMEOUT_MS,
    baseUrl: DEFAULT_BASE_URL,
    adminEmail: 'e2e-admin@comuki.local',
    adminPassword: 'comuki-e2e-dev-only',
    help: false,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    if (raw === '--help' || raw === '-h') {
      options.help = true;
      continue;
    }
    if (raw === '--skip-build') {
      options.skipBuild = true;
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
    if (flag === '--base-url') {
      if (v === undefined) {
        return { ok: false, error: '--base-url needs a URL value' };
      }
      options.baseUrl = v;
      continue;
    }
    if (flag === '--admin-email') {
      if (v === undefined) {
        return { ok: false, error: '--admin-email needs a value' };
      }
      options.adminEmail = v;
      continue;
    }
    if (flag === '--admin-password') {
      if (v === undefined) {
        return { ok: false, error: '--admin-password needs a value' };
      }
      options.adminPassword = v;
      continue;
    }
    return { ok: false, error: `unrecognized argument: ${raw}` };
  }

  return { ok: true, options };
}

// ---------------------------------------------------------------------------
// Compose-cmd detection
// ---------------------------------------------------------------------------

/**
 * Auto-detect the compose binary: prefer podman (this repo's documented
 * runtime), fall back to docker. A binary that prints a non-zero exit
 * code on `compose version` is treated as not-present (mirrors how a
 * fresh Podman install without the docker compose plugin reports
 * "looking up compose provider failed" with exit 1).
 *
 * @param {{ run: typeof spawnSync, env?: NodeJS.ProcessEnv }} [deps]
 * @returns {string} 'podman' or 'docker'
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
// Image build
// ---------------------------------------------------------------------------

/**
 * Build comuki-test-fake-model:e2e via raw `podman build --ignorefile` —
 * compose has no YAML key to pass a custom ignorefile, so e2e-up does this
 * out of band before invoking compose at all. The Dockerfile's own header
 * documents this exact invocation; we replicate it scripted.
 *
 * @param {{ run: typeof spawnSync, env?: NodeJS.ProcessEnv, image?: string, dockerfile?: string, ignorefile?: string, cwd?: string }} [deps]
 * @returns {{ status: number, stdout: string, stderr: string, error: Error | null }}
 */
export function buildTestFakeModel({
  run = spawnSync,
  env = process.env,
  image = TEST_FAKE_MODEL_IMAGE,
  dockerfile = TEST_FAKE_MODEL_DOCKERFILE,
  ignorefile = TEST_FAKE_MODEL_IGNORE,
  cwd = REPO_ROOT,
} = {}) {
  const args = [
    'build',
    '--ignorefile', ignorefile,
    '-f', dockerfile,
    '-t', image,
    '.',
  ];
  return run('podman', args, {
    cwd,
    env,
    stdio: ['ignore', 'inherit', 'inherit'],
    encoding: 'utf8',
  });
}

// ---------------------------------------------------------------------------
// Compose up + log dump on failure
// ---------------------------------------------------------------------------

/**
 * Run `podman compose -f deploy/compose.e2e.yml up -d --wait --wait-timeout <n>`.
 * Returns the spawn result. On non-zero exit, the caller is responsible for
 * printing logs (this function doesn't, by design — it lets the caller decide
 * whether to render to stdout/stderr or capture into a file).
 *
 * @param {{ cmd: string, run?: typeof spawnSync, file?: string, env?: NodeJS.ProcessEnv, cwd?: string, waitTimeoutSeconds?: number }} input
 */
export function composeUp({
  cmd,
  run = spawnSync,
  file = COMPOSE_FILE,
  env = process.env,
  cwd = REPO_ROOT,
  waitTimeoutSeconds = COMPOSE_UP_WAIT_TIMEOUT,
}) {
  const args = [
    'compose',
    '-f', file,
    'up',
    '-d',
    '--wait',
    '--wait-timeout', String(waitTimeoutSeconds),
  ];
  return run(cmd, args, {
    cwd,
    env,
    stdio: ['ignore', 'inherit', 'inherit'],
    encoding: 'utf8',
  });
}

/**
 * Dump the last 200 lines of every service's log to stderr. Best-effort —
 * log dump failures do not throw, they just print a small note.
 *
 * @param {{ cmd: string, run?: typeof spawnSync, file?: string, env?: NodeJS.ProcessEnv, cwd?: string, stderr?: NodeJS.WritableStream }} input
 */
export function dumpServiceLogs({
  cmd,
  run = spawnSync,
  file = COMPOSE_FILE,
  env = process.env,
  cwd = REPO_ROOT,
  stderr,
}) {
  const args = ['compose', '-f', file, 'logs', '--tail=200'];
  const result = run(cmd, args, {
    cwd,
    env,
    stdio: ['ignore', 'pipe', 'pipe'],
    encoding: 'utf8',
  });
  const sink = stderr ?? process.stderr;
  sink.write('--- compose logs (tail 200) ---\n');
  sink.write(`${result.stdout ?? ''}${result.stderr ?? ''}`);
  sink.write('\n--- end compose logs ---\n');
}

// ---------------------------------------------------------------------------
// Login + API key bootstrap
// ---------------------------------------------------------------------------

/**
 * POST /api/v1/auth/login. Sets a session cookie on the shared `cookieJar`
 * (a plain object whose `.cookie` field is appended to — the host returns
 * `Set-Cookie: ...` and the next call needs it). 200 + userId/email on
 * success; throws on any non-200.
 *
 * @param {{ baseUrl: string, email: string, password: string, fetchImpl?: typeof fetch, cookieJar?: { cookie: string }, signal?: AbortSignal }} input
 */
export async function loginAsync({
  baseUrl,
  email,
  password,
  fetchImpl = fetch,
  cookieJar = { cookie: '' },
  signal,
}) {
  const response = await fetchImpl(`${baseUrl}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
    signal,
  });
  appendSetCookie(response, cookieJar);
  if (response.status !== 200) {
    const body = await response.text();
    throw new Error(`login failed: HTTP ${response.status} — ${body}`);
  }
  return /** @type {{ userId: string, email: string, displayName: string }} */ (await response.json());
}

/**
 * GET /api/v1/auth/me. Reuses the cookie jar from login.
 * @param {{ baseUrl: string, fetchImpl?: typeof fetch, cookieJar?: { cookie: string }, signal?: AbortSignal }} input
 */
export async function fetchMeAsync({
  baseUrl,
  fetchImpl = fetch,
  cookieJar,
  signal,
}) {
  const response = await fetchImpl(`${baseUrl}/api/v1/auth/me`, {
    headers: withAuthHeaders({}, cookieJar),
    signal,
  });
  if (response.status !== 200) {
    const body = await response.text();
    throw new Error(`auth/me failed: HTTP ${response.status} — ${body}`);
  }
  return await response.json();
}

/**
 * POST /api/v1/keys. Returns { keyId, secret } — the secret is the bearer
 * token the smoke script will use on every subsequent call. Requires the
 * bootstrap admin's `identity:write` permission (the admin has it).
 *
 * @param {{ baseUrl: string, userId: string, fetchImpl?: typeof fetch, cookieJar?: { cookie: string }, signal?: AbortSignal, label?: string }} input
 */
export async function stampApiKeyAsync({
  baseUrl,
  userId,
  fetchImpl = fetch,
  cookieJar,
  signal,
  label = 'e2e-smoke',
}) {
  const response = await fetchImpl(`${baseUrl}/api/v1/keys`, {
    method: 'POST',
    headers: withAuthHeaders({ 'Content-Type': 'application/json' }, cookieJar),
    body: JSON.stringify({ userId, expiresAt: null, tenantProjectId: null, label }),
    signal,
  });
  if (response.status !== 201) {
    const body = await response.text();
    throw new Error(`keys POST failed: HTTP ${response.status} — ${body}`);
  }
  const json = await response.json();
  if (typeof json.secret !== 'string' || json.secret.length === 0) {
    throw new Error(`keys POST returned no secret: ${JSON.stringify(json)}`);
  }
  return /** @type {{ keyId: string, prefix: string, secret: string }} */ (json);
}

// ---------------------------------------------------------------------------
// Cookie jar
// ---------------------------------------------------------------------------

/**
 * @param {Response} response
 * @param {{ cookie: string }} cookieJar
 */
function appendSetCookie(response, cookieJar) {
  // Node's undici fetch returns getSetCookie() on the Headers; older runtimes
  // expose only `get('set-cookie')`. Both shapes land here.
  const headers = response.headers;
  /** @type {string[] | null} */
  let setCookies = null;
  if (typeof headers.getSetCookie === 'function') {
    setCookies = headers.getSetCookie();
  } else {
    const raw = headers.get('set-cookie');
    setCookies = raw === null ? null : [raw];
  }
  if (setCookies === null || setCookies.length === 0) {
    return;
  }
  for (const c of setCookies) {
    // keep the name=value portion (drop attributes). Multiple Set-Cookie
    // values are appended separated by '; '.
    const nameValue = c.split(';')[0].trim();
    if (nameValue.length === 0) {
      continue;
    }
    cookieJar.cookie = cookieJar.cookie.length === 0
      ? nameValue
      : `${cookieJar.cookie}; ${nameValue}`;
  }
}

/**
 * @param {Record<string, string>} headers
 * @param {{ cookie: string }} cookieJar
 */
function withAuthHeaders(headers, cookieJar) {
  const out = { ...headers };
  if (cookieJar.cookie.length > 0) {
    out['Cookie'] = cookieJar.cookie;
  }
  return out;
}

// ---------------------------------------------------------------------------
// Bootstrap orchestration
// ---------------------------------------------------------------------------

/**
 * Log in as the bootstrap admin and stamp an API key. Returns the values
 * written into artifacts/e2e/bootstrap.json.
 *
 * @param {{ baseUrl: string, email: string, password: string, fetchImpl?: typeof fetch, deadlineMs?: number, label?: string }} input
 */
export async function bootstrapAdminKeyAsync({
  baseUrl,
  email,
  password,
  fetchImpl = fetch,
  deadlineMs = 30_000,
  label = 'e2e-smoke',
}) {
  const cookieJar = { cookie: '' };
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), deadlineMs);
  try {
    const login = await loginAsync({ baseUrl, email, password, fetchImpl, cookieJar, signal: controller.signal });
    const me = await fetchMeAsync({ baseUrl, fetchImpl, cookieJar, signal: controller.signal });
    const key = await stampApiKeyAsync({
      baseUrl,
      userId: login.userId,
      fetchImpl,
      cookieJar,
      signal: controller.signal,
      label,
    });
    return {
      baseUrl,
      apiKey: key.secret,
      userId: login.userId,
      keyId: key.keyId,
      createdAt: new Date().toISOString(),
    };
  } finally {
    clearTimeout(timer);
  }
}

// ---------------------------------------------------------------------------
// Health probe (compose up + small grace before login)
// ---------------------------------------------------------------------------

/**
 * Poll GET /api/v1/health until 200 or deadline elapses. The compose file
 * already runs `up --wait --wait-timeout N` (waits for all healthchecks to
 * pass), but the host's own healthcheck (`curl /api/v1/health`) only fires
 * every 15s with a 30s start_period; an extra few seconds of grace is
 * belt-and-suspenders.
 *
 * @param {{ baseUrl: string, fetchImpl?: typeof fetch, deadlineMs?: number, intervalMs?: number }} input
 */
export async function waitForHealthyAsync({
  baseUrl,
  fetchImpl = fetch,
  deadlineMs = 60_000,
  intervalMs = 1_000,
}) {
  const controller = new AbortController();
  const deadline = Date.now() + deadlineMs;
  let lastError = null;
  while (Date.now() < deadline) {
    if (controller.signal.aborted) {
      break;
    }
    try {
      const response = await fetchImpl(`${baseUrl}/api/v1/health`, { signal: controller.signal });
      if (response.status === 200) {
        return;
      }
      lastError = new Error(`health returned ${response.status}`);
    } catch (err) {
      lastError = err;
    }
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }
  throw new Error(`host did not become healthy within ${deadlineMs}ms${lastError ? ` (last error: ${lastError.message})` : ''}`);
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

/**
 * @param {string[]} argv
 * @param {{
 *   run?: typeof spawnSync,
 *   fetchImpl?: typeof fetch,
 *   stdout?: NodeJS.WritableStream,
 *   stderr?: NodeJS.WritableStream,
 *   env?: NodeJS.ProcessEnv,
 *   cwd?: string,
 *   repoRoot?: string,
 *   buildTestFakeModelFn?: typeof buildTestFakeModel,
 *   composeUpFn?: typeof composeUp,
 *   bootstrapFn?: typeof bootstrapAdminKeyAsync,
 *   waitForHealthyFn?: typeof waitForHealthyAsync,
 *   dumpLogsFn?: typeof dumpServiceLogs,
 *   detectComposeCmdFn?: typeof detectComposeCmd,
 * }} [deps]
 */
export async function main(argv, deps = {}) {
  const stdout = deps.stdout ?? process.stdout;
  const stderr = deps.stderr ?? process.stderr;
  const run = deps.run ?? spawnSync;
  const fetchImpl = deps.fetchImpl ?? fetch;
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

  let composeCmd;
  try {
    composeCmd = options.composeCmd === 'auto'
      ? (deps.detectComposeCmdFn ?? detectComposeCmd)({ run, env })
      : options.composeCmd;
  } catch (err) {
    stderr.write(`e2e-up: ${err.message}\n`);
    return 1;
  }
  stdout.write(`e2e-up: compose-cmd = ${composeCmd}\n`);
  stdout.write(`e2e-up: timeout = ${options.timeoutMs}ms\n`);

  const overallDeadline = Date.now() + options.timeoutMs;

  // 1. Image builds
  if (!options.skipBuild) {
    stdout.write(`e2e-up: building ${TEST_FAKE_MODEL_IMAGE} (raw podman build --ignorefile, see Dockerfile header)…\n`);
    const buildResult = (deps.buildTestFakeModelFn ?? buildTestFakeModel)({ run, env, cwd });
    if (buildResult.error !== null && buildResult.error !== undefined) {
      stderr.write(`e2e-up: failed to spawn podman build: ${buildResult.error.message}\n`);
      return 1;
    }
    if (buildResult.status !== 0) {
      stderr.write(`e2e-up: podman build exited ${buildResult.status}\n`);
      return 1;
    }
    if (Date.now() > overallDeadline) {
      stderr.write('e2e-up: timed out after TestFakeModel build; not running compose up\n');
      return 1;
    }
  } else {
    stdout.write('e2e-up: --skip-build set, skipping TestFakeModel build\n');
  }

  // 2. Compose up
  stdout.write(`e2e-up: ${composeCmd} compose up -d --wait…\n`);
  const upResult = (deps.composeUpFn ?? composeUp)({ cmd: composeCmd, run, env, cwd });
  if (upResult.error !== null && upResult.error !== undefined) {
    stderr.write(`e2e-up: failed to spawn compose up: ${upResult.error.message}\n`);
    return 1;
  }
  if (upResult.status !== 0) {
    stderr.write(`e2e-up: ${composeCmd} compose up exited ${upResult.status}\n`);
    (deps.dumpLogsFn ?? dumpServiceLogs)({ cmd: composeCmd, run, env, cwd, stderr });
    return 1;
  }

  // 3. Health probe + bootstrap
  if (Date.now() > overallDeadline) {
    stderr.write('e2e-up: timed out before health probe\n');
    (deps.dumpLogsFn ?? dumpServiceLogs)({ cmd: composeCmd, run, env, cwd, stderr });
    return 1;
  }

  try {
    await (deps.waitForHealthyFn ?? waitForHealthyAsync)({
      baseUrl: options.baseUrl,
      fetchImpl,
      deadlineMs: Math.max(5_000, overallDeadline - Date.now()),
    });
  } catch (err) {
    stderr.write(`e2e-up: ${err.message}\n`);
    (deps.dumpLogsFn ?? dumpServiceLogs)({ cmd: composeCmd, run, env, cwd, stderr });
    return 1;
  }

  if (Date.now() > overallDeadline) {
    stderr.write('e2e-up: timed out before login\n');
    return 1;
  }

  let bootstrap;
  try {
    bootstrap = await (deps.bootstrapFn ?? bootstrapAdminKeyAsync)({
      baseUrl: options.baseUrl,
      email: options.adminEmail,
      password: options.adminPassword,
      fetchImpl,
      deadlineMs: Math.max(5_000, overallDeadline - Date.now()),
    });
  } catch (err) {
    stderr.write(`e2e-up: bootstrap failed: ${err.message}\n`);
    (deps.dumpLogsFn ?? dumpServiceLogs)({ cmd: composeCmd, run, env, cwd, stderr });
    return 1;
  }

  // baseUrl in bootstrap.json reflects the value the caller passed in
  // (default or --base-url) — keeps the smoke script portable across
  // docker-host aliases (localhost / host.docker.internal / remote IP).
  const out = { ...bootstrap, baseUrl: options.baseUrl };

  mkdirSync(path.dirname(BOOTSTRAP_PATH), { recursive: true });
  writeFileSync(BOOTSTRAP_PATH, `${JSON.stringify(out, null, 2)}\n`);

  stdout.write(`e2e-up: stack is up; bootstrap admin API key stamped.\n`);
  stdout.write(`e2e-up: base url = ${options.baseUrl}\n`);
  stdout.write(`e2e-up: bootstrap = ${path.relative(REPO_ROOT, BOOTSTRAP_PATH)}\n`);

  return 0;
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  main(process.argv.slice(2)).then(
    (code) => { process.exitCode = code; },
    (err) => {
      process.stderr.write(`e2e-up: ${err.stack ?? err.message}\n`);
      process.exitCode = 1;
    },
  );
}