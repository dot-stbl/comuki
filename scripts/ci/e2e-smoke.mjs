#!/usr/bin/env node
/**
 * e2e-smoke — WS13 one-scenario smoke test (openspec add-agentic-test-contour).
 *
 * Drives a single webhook→run flow against the live API exposed by the
 * compose stack e2e-up.mjs brought up: logs in as the bootstrap admin,
 * creates a fresh project, a github source connection, a watch admission
 * rule, posts a signed issue-opened webhook, polls for the run to reach a
 * terminal state (or a weaker "compute-start" milestone — see
 * --assert-through), then writes report.json/report.md via the same
 * envelope helpers scripts/ci/dotnet-test.mjs exports. Tier on the report
 * is `e2e-smoke`, mode is `fake` (the worker never makes a real model
 * call — TestFakePi replaces pi entirely, see
 * tests/tools/Comuki.TestFakePi/worker-test.Dockerfile).
 *
 * Usage:
 *   node scripts/ci/e2e-smoke.mjs
 *   node scripts/ci/e2e-smoke.mjs --base-url=http://localhost:17180 --api-key=ck_...
 *   node scripts/ci/e2e-smoke.mjs --assert-through=compute-start
 *   node scripts/ci/e2e-smoke.mjs --timeout=600000
 *   node scripts/ci/e2e-smoke.mjs --help
 *
 * Defaults to reading baseUrl + apiKey from artifacts/e2e/bootstrap.json
 * (the file e2e-up.mjs wrote). When neither flag nor bootstrap.json is
 * available, performs the bootstrap admin login itself with the same
 * defaults e2e-up.mjs uses, so the script is independently runnable
 * against an already-up stack.
 *
 * --assert-through:
 *   terminal       (default) PASS only if status == 'succeeded'. Anything
 *                   else (failed/cancelled/timeout) is a hard FAIL.
 *   compute-start  PASS if either status == 'succeeded' OR a journal
 *                   WorkspacePrepared evidence was observed (a work item
 *                   reached 'running' or any work_item.status_changed
 *                   event's payload contains 'Running' at any point
 *                   during polling). FAIL only if NEITHER terminal
 *                   success NOR that weaker evidence appeared. This is
 *                   the fallback for the #153 socket-fix path; the
 *                   orchestrator picks the actual default during live
 *                   validation per the WS13 brief.
 *
 * Zero npm dependencies — bun/node builtins only. Same shape as
 * scripts/ci/live-eval.mjs / e2e-up.mjs (exported pure helpers +
 * injectable deps + main() returning an exit code).
 */
import { spawnSync } from 'node:child_process';
import { createHmac, randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import {
  buildEnvelope,
  formatVerdict,
  renderMarkdown,
} from './dotnet-test.mjs';

const __filename = fileURLToPath(import.meta.url);
const REPO_ROOT = path.resolve(path.dirname(__filename), '..', '..');

const BOOTSTRAP_PATH = path.join(REPO_ROOT, 'artifacts', 'e2e', 'bootstrap.json');
const WEBHOOK_FIXTURE_PATH = path.join(
  REPO_ROOT,
  'tests',
  'integration',
  'Comuki.Host.Integration.Intake',
  'Fixtures',
  'github-issue-opened.json',
);

const DEFAULT_BASE_URL = 'http://localhost:17180';
const DEFAULT_TIMEOUT_MS = 300_000;           // 5 min overall poll budget
const DEFAULT_POLL_INTERVAL_MS = 1_000;       // 1s between polls
const DEFAULT_ASSERT_THROUGH = 'terminal';      // orchestrator may revise
const DEFAULT_BOOTSTRAP_ADMIN_EMAIL = 'e2e-admin@comuki.local';
const DEFAULT_BOOTSTRAP_ADMIN_PASSWORD = 'comuki-e2e-dev-only';
const DEFAULT_WEBHOOK_SECRET = 'e2e-smoke-hook-secret';
const DEFAULT_REPORT_DIR = path.join(REPO_ROOT, 'artifacts', 'test-reports', 'e2e-smoke');

const TERMINAL_STATUSES = new Set(['succeeded', 'failed', 'cancelled']);

const USAGE = `e2e-smoke — WS13 one-scenario smoke test

Usage:
  node scripts/ci/e2e-smoke.mjs [--base-url=<url>] [--api-key=<key>]
                                 [--timeout=<ms>]
                                 [--assert-through=<terminal|compute-start>]
                                 [--report-dir=<dir>]
                                 [--admin-email=<e>] [--admin-password=<p>]
  node scripts/ci/e2e-smoke.mjs --help

Options:
  --base-url=<url>              Host base URL. Default: http://localhost:17180
                                (matches compose.e2e.yml's published port).
  --api-key=<key>               Bearer API key. If absent, the script reads
                                artifacts/e2e/bootstrap.json; if that's also
                                missing, it logs in as the bootstrap admin
                                itself (--admin-email/--admin-password).
  --timeout=<ms>               Overall poll budget. Default: 300000 (5 min).
  --assert-through=<level>      'terminal' (default) — only 'succeeded' PASSes.
                                'compute-start' — also PASSes when journal
                                shows a work item reaching 'running' (the
                                weaker evidence used by WS11/#153).
  --report-dir=<dir>            Where report.json + report.md land.
                                Default: artifacts/test-reports/e2e-smoke/.
  --admin-email=<e>             Bootstrap admin email used when neither
                                --api-key nor bootstrap.json is present.
                                Default: e2e-admin@comuki.local.
  --admin-password=<p>          Bootstrap admin password (same).
                                Default: comuki-e2e-dev-only.

Notes:
  - 'terminal' is the default assertion level — orchestrator may revise it
    after live validation per the WS13 brief.
  - The script NEVER auto-runs the stack (no e2e-up here). Bring the stack
    up yourself first, or pass --base-url pointing at any reachable host.
  - Report shape matches scripts/ci/dotnet-test.mjs's envelope: schema
    1, tier 'e2e-smoke', mode 'fake'. One scenario entry named 'e2e-smoke'.
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
    baseUrl: undefined,
    apiKey: undefined,
    timeoutMs: DEFAULT_TIMEOUT_MS,
    assertThrough: DEFAULT_ASSERT_THROUGH,
    reportDir: DEFAULT_REPORT_DIR,
    adminEmail: DEFAULT_BOOTSTRAP_ADMIN_EMAIL,
    adminPassword: DEFAULT_BOOTSTRAP_ADMIN_PASSWORD,
    help: false,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    if (raw === '--help' || raw === '-h') {
      options.help = true;
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
    if (flag === '--base-url') {
      if (v === undefined) {
        return { ok: false, error: '--base-url needs a URL value' };
      }
      options.baseUrl = v;
      continue;
    }
    if (flag === '--api-key') {
      if (v === undefined) {
        return { ok: false, error: '--api-key needs a value' };
      }
      options.apiKey = v;
      continue;
    }
    if (flag === '--timeout') {
      if (v === undefined || !/^\d+$/.test(v)) {
        return { ok: false, error: '--timeout must be a positive integer (ms)' };
      }
      options.timeoutMs = Number(v);
      continue;
    }
    if (flag === '--assert-through') {
      if (v === undefined || !['terminal', 'compute-start'].includes(v)) {
        return { ok: false, error: '--assert-through must be terminal|compute-start' };
      }
      options.assertThrough = v;
      continue;
    }
    if (flag === '--report-dir') {
      if (v === undefined) {
        return { ok: false, error: '--report-dir needs a path' };
      }
      options.reportDir = path.isAbsolute(v)
        ? v
        : path.join(REPO_ROOT, v);
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
// Bootstrap.json reading
// ---------------------------------------------------------------------------

/**
 * @param {string} [filePath]
 * @returns {{ baseUrl: string, apiKey: string } | null}
 */
export function readBootstrap(filePath = BOOTSTRAP_PATH) {
  if (!existsSync(filePath)) {
    return null;
  }
  try {
    const raw = JSON.parse(readFileSync(filePath, 'utf8'));
    if (typeof raw.baseUrl !== 'string' || typeof raw.apiKey !== 'string') {
      return null;
    }
    return { baseUrl: raw.baseUrl, apiKey: raw.apiKey };
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// HTTP helpers
// ---------------------------------------------------------------------------

/**
 * @typedef {{
 *   baseUrl: string,
 *   apiKey: string,
 *   fetchImpl?: typeof fetch,
 *   signal?: AbortSignal,
 * }} HttpCtx
 */

/**
 * @param {HttpCtx} ctx
 * @param {string} path
 * @param {RequestInit} [init]
 */
async function httpJsonAsync({ baseUrl, apiKey, fetchImpl = fetch, signal }, path, init = {}) {
  const headers = { ...(init.headers ?? {}), Authorization: `Bearer ${apiKey}` };
  const response = await fetchImpl(`${baseUrl}${path}`, { ...init, headers, signal });
  const text = await response.text();
  let body;
  if (text.length === 0) {
    body = null;
  } else {
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  }
  return { status: response.status, body };
}

// ---------------------------------------------------------------------------
// Scenario steps
// ---------------------------------------------------------------------------

/**
 * Generate a short random suffix for unique project slugs per run.
 * @returns {string}
 */
export function randomSlugSuffix() {
  return randomUUID().slice(0, 8);
}

/**
 * @param {HttpCtx} ctx
 * @param {{ slugSuffix: string }} input
 * @returns {Promise<{ projectId: string, projectView: object }>}
 */
export async function createProjectAsync(ctx, { slugSuffix }) {
  const slug = `e2e-smoke-${slugSuffix}`;
  const result = await httpJsonAsync(ctx, '/api/v1/projects', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      name: `e2e smoke ${slugSuffix}`,
      slug,
      description: null,
      profilesGitUrl: null,
      profilesGitRef: null,
    }),
  });
  if (result.status !== 201) {
    throw new Error(`createProject failed: HTTP ${result.status} — ${JSON.stringify(result.body)}`);
  }
  // ProjectId on this ONE response type serializes as a NESTED object
  // { "value": "<guid>" } (no custom JSON converter on the ProjectId wrapper
  // type). Verified live; different from every other id in this API.
  const idValue = result.body?.id?.value;
  if (typeof idValue !== 'string') {
    throw new Error(`createProject returned no id.value: ${JSON.stringify(result.body)}`);
  }
  return { projectId: idValue, projectView: result.body };
}

/**
 * @param {HttpCtx} ctx
 * @param {{ projectId: string, secretEnvRef: string }} input
 * @returns {Promise<{ webhookPath: string }>}
 */
export async function createSourceConnectionAsync(ctx, { projectId, secretEnvRef }) {
  const result = await httpJsonAsync(ctx, '/api/v1/sources', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      projectId,
      provider: 'github',
      name: 'e2e smoke hook',
      settingsJson: '{"owner":"dot-stbl","repo":"comuki"}',
      secretEnvRef,
    }),
  });
  if (result.status !== 201) {
    throw new Error(`createSource failed: HTTP ${result.status} — ${JSON.stringify(result.body)}`);
  }
  if (typeof result.body?.webhookPath !== 'string') {
    throw new Error(`createSource returned no webhookPath: ${JSON.stringify(result.body)}`);
  }
  return { webhookPath: result.body.webhookPath };
}

/**
 * @param {HttpCtx} ctx
 * @param {{ projectId: string }} input
 */
export async function createAdmissionRuleAsync(ctx, { projectId }) {
  const result = await httpJsonAsync(ctx, '/api/v1/admission-rules', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      projectId,
      mode: 'watch',
      filterJson: '{"labelsAny":["comuki"]}',
    }),
  });
  if (result.status !== 201) {
    throw new Error(`createAdmissionRule failed: HTTP ${result.status} — ${JSON.stringify(result.body)}`);
  }
}

/**
 * Sign a payload with HMAC-SHA256, key = webhookSecret, return
 * "sha256=" + hex (GitHub's X-Hub-Signature-256 format).
 * @param {string} payload
 * @param {string} secret
 * @returns {string}
 */
export function signWebhookPayload(payload, secret = DEFAULT_WEBHOOK_SECRET) {
  const hex = createHmac('sha256', secret).update(payload).digest('hex');
  return `sha256=${hex}`;
}

/**
 * @param {{ baseUrl: string, webhookPath: string, rawBody: string, signature: string, deliveryId: string, fetchImpl?: typeof fetch, signal?: AbortSignal }} input
 */
export async function postWebhookAsync({
  baseUrl,
  webhookPath,
  rawBody,
  signature,
  deliveryId,
  fetchImpl = fetch,
  signal,
}) {
  const response = await fetchImpl(`${baseUrl}${webhookPath}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-GitHub-Delivery': deliveryId,
      'X-GitHub-Event': 'issues',
      'X-Hub-Signature-256': signature,
    },
    body: rawBody,
    signal,
  });
  const text = await response.text();
  let body;
  try {
    body = JSON.parse(text);
  } catch {
    body = text;
  }
  return { status: response.status, body };
}

/**
 * @param {HttpCtx} ctx
 * @param {{ projectId: string }} input
 * @returns {Promise<Array<{ id: string, projectId: string, status: string, ... }>>}
 */
export async function listRunsAsync(ctx, { projectId }) {
  const result = await httpJsonAsync(ctx, '/api/v1/runs?pageSize=5', { method: 'GET' });
  if (result.status !== 200) {
    throw new Error(`listRuns failed: HTTP ${result.status} — ${JSON.stringify(result.body)}`);
  }
  const items = Array.isArray(result.body?.items) ? result.body.items : [];
  // Filter client-side: the API exposes no projectId query filter we
  // can trust (verified empirically — see WebhooksShould in the repo).
  return items.filter((r) => r.projectId === projectId);
}

/**
 * @param {HttpCtx} ctx
 * @param {{ runId: string }} input
 */
export async function getRunAsync(ctx, { runId }) {
  const result = await httpJsonAsync(ctx, `/api/v1/runs/${runId}`, { method: 'GET' });
  if (result.status !== 200) {
    throw new Error(`getRun failed: HTTP ${result.status} — ${JSON.stringify(result.body)}`);
  }
  return result.body;
}

/**
 * @param {HttpCtx} ctx
 * @param {{ projectId: string, runId: string }} input
 */
export async function listArtifactsAsync(ctx, { projectId, runId }) {
  const result = await httpJsonAsync(
    ctx,
    `/api/v1/projects/${projectId}/runs/${runId}/artifacts`,
    { method: 'GET' },
  );
  if (result.status !== 200) {
    throw new Error(`listArtifacts failed: HTTP ${result.status} — ${JSON.stringify(result.body)}`);
  }
  return result.body;
}

// ---------------------------------------------------------------------------
// Polling + verdict
// ---------------------------------------------------------------------------

/**
 * Determine whether `run` shows the "WorkspacePrepared" milestone
 * (any work_item.status_changed event whose payload contains "Running",
 * or any workItem in 'running' status). Mirrors the platform's own
 * scenario-test harness (tests/tools/Comuki.AgentTest.Runner/Journal/
 * JournalConditionEvaluator.cs).
 *
 * @param {{ status?: string, events?: Array<{ type?: string, payloadJson?: string }>, workItems?: Array<{ status?: string }> }} run
 * @returns {boolean}
 */
export function hasComputeStartEvidence(run) {
  if (run === null || run === undefined) {
    return false;
  }
  if (Array.isArray(run.workItems)) {
    for (const item of run.workItems) {
      if (item?.status === 'running') {
        return true;
      }
    }
  }
  if (Array.isArray(run.events)) {
    for (const evt of run.events) {
      if (evt?.type !== 'work_item.status_changed') {
        continue;
      }
      if (typeof evt.payloadJson !== 'string') {
        continue;
      }
      if (evt.payloadJson.includes('Running')) {
        return true;
      }
    }
  }
  return false;
}

/**
 * @typedef {{
 *   outcome: 'pass' | 'fail',
 *   reason: string,
 *   runStatus?: string,
 *   sawComputeStart?: boolean,
 * }} SmokeVerdict
 */

/**
 * @param {{ assertThrough: 'terminal'|'compute-start', run: object | null, sawComputeStart: boolean }} input
 * @returns {SmokeVerdict}
 */
export function evaluateVerdict({ assertThrough, run, sawComputeStart }) {
  const status = run?.status;
  if (assertThrough === 'terminal') {
    if (status === 'succeeded') {
      return { outcome: 'pass', reason: "run reached status 'succeeded'", runStatus: status, sawComputeStart };
    }
    return {
      outcome: 'fail',
      reason: `run status is '${status ?? 'unknown'}' (assert-through=terminal requires 'succeeded')`,
      runStatus: status,
      sawComputeStart,
    };
  }
  // compute-start
  if (status === 'succeeded') {
    return { outcome: 'pass', reason: "run reached status 'succeeded'", runStatus: status, sawComputeStart };
  }
  if (sawComputeStart) {
    return {
      outcome: 'pass',
      reason: "compute-start evidence observed (work item reached 'running')",
      runStatus: status,
      sawComputeStart,
    };
  }
  return {
    outcome: 'fail',
    reason: `run status is '${status ?? 'unknown'}' and no compute-start evidence was observed`,
    runStatus: status,
    sawComputeStart,
  };
}

/**
 * Poll listRuns until a run for this projectId appears, then getRun
 * until status becomes terminal (or compute-start evidence is seen in
 * the events list, for the weaker assertion level) or the deadline
 * elapses.
 *
 * @param {{
 *   ctx: HttpCtx,
 *   projectId: string,
 *   deadlineMs: number,
 *   intervalMs?: number,
 *   assertThrough: 'terminal'|'compute-start',
 *   fetchImpl?: typeof fetch,
 *   sleepFn?: (ms: number) => Promise<void>,
 *   nowFn?: () => number,
 * }} input
 * @returns {Promise<{ run: object | null, sawComputeStart: boolean, ranOutOfTime: boolean, lastStatus?: string }>}
 */
export async function pollRunUntilTerminalAsync({
  ctx,
  projectId,
  deadlineMs,
  intervalMs = DEFAULT_POLL_INTERVAL_MS,
  assertThrough,
  fetchImpl = fetch,
  sleepFn = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  nowFn = Date.now,
}) {
  const deadline = nowFn() + deadlineMs;
  let sawComputeStart = false;
  let observedRun = null;
  let lastStatus;

  while (nowFn() < deadline) {
    const runs = await listRunsAsync({ ...ctx, fetchImpl }, { projectId });
    if (runs.length > 0) {
      const runId = runs[0].id;
      const run = await getRunAsync({ ...ctx, fetchImpl }, { runId });
      observedRun = run;
      lastStatus = run?.status;
      if (hasComputeStartEvidence(run)) {
        sawComputeStart = true;
      }
      if (TERMINAL_STATUSES.has(lastStatus ?? '')) {
        return { run, sawComputeStart, ranOutOfTime: false, lastStatus };
      }
      if (sawComputeStart && assertThrough === 'compute-start') {
        return { run, sawComputeStart, ranOutOfTime: false, lastStatus };
      }
    }
    await sleepFn(intervalMs);
  }
  return { run: observedRun, sawComputeStart, ranOutOfTime: true, lastStatus };
}

// ---------------------------------------------------------------------------
// Webhook fixture
// ---------------------------------------------------------------------------

/**
 * @param {string} [filePath]
 * @returns {string} raw JSON text
 */
export function loadWebhookFixture(filePath = WEBHOOK_FIXTURE_PATH) {
  return readFileSync(filePath, 'utf8');
}

// ---------------------------------------------------------------------------
// Scenario orchestration
// ---------------------------------------------------------------------------

/**
 * Run the full webhook→run smoke flow against a live stack. Returns
 * the report envelope (without the `failures` populated yet) and the
 * final verdict + last seen run. Throws only on infrastructure errors
 * (login failure, project creation failure, etc.) — assertion failures
 * are returned, not thrown.
 *
 * @param {{
 *   ctx: HttpCtx,
 *   assertThrough: 'terminal'|'compute-start',
 *   timeoutMs: number,
 *   pollIntervalMs?: number,
 *   fixturePath?: string,
 *   webhookSecret?: string,
 *   fetchImpl?: typeof fetch,
 *   sleepFn?: (ms: number) => Promise<void>,
 *   nowFn?: () => number,
 *   slugSuffix?: string,
 * }} input
 */
export async function runSmokeAsync({
  ctx,
  assertThrough,
  timeoutMs,
  pollIntervalMs = DEFAULT_POLL_INTERVAL_MS,
  fixturePath = WEBHOOK_FIXTURE_PATH,
  webhookSecret = DEFAULT_WEBHOOK_SECRET,
  fetchImpl = fetch,
  sleepFn = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  nowFn = Date.now,
  slugSuffix = randomSlugSuffix(),
}) {
  const startedAtIso = new Date().toISOString();
  const start = nowFn();

  // 1. Create project
  const { projectId } = await createProjectAsync({ ...ctx, fetchImpl }, { slugSuffix });

  // 2. Create source connection (Github webhook)
  const { webhookPath } = await createSourceConnectionAsync(
    { ...ctx, fetchImpl },
    { projectId, secretEnvRef: 'COMUKI_E2E_GH_HOOK_SECRET' },
  );

  // 3. Create admission rule (watch, label = comuki)
  await createAdmissionRuleAsync({ ...ctx, fetchImpl }, { projectId });

  // 4. Sign + POST the webhook
  const rawBody = loadWebhookFixture(fixturePath);
  const signature = signWebhookPayload(rawBody, webhookSecret);
  const deliveryId = randomUUID();
  const webhookResponse = await postWebhookAsync({
    baseUrl: ctx.baseUrl,
    webhookPath,
    rawBody,
    signature,
    deliveryId,
    fetchImpl,
  });
  if (webhookResponse.status !== 200) {
    throw new Error(`webhook rejected: HTTP ${webhookResponse.status} — ${JSON.stringify(webhookResponse.body)}`);
  }
  if (webhookResponse.body?.outcome !== 'admitted') {
    throw new Error(`webhook outcome is not 'admitted': ${JSON.stringify(webhookResponse.body)}`);
  }

  // 5. Poll for run terminal
  const deadlineMs = Math.max(1_000, timeoutMs - (nowFn() - start));
  const poll = await pollRunUntilTerminalAsync({
    ctx: { ...ctx, fetchImpl },
    fetchImpl,
    projectId,
    deadlineMs,
    intervalMs: pollIntervalMs,
    assertThrough,
    sleepFn,
    nowFn,
  });

  // 6. Artifacts (best-effort — empty list is normal for failed/cancelled runs)
  let artifactsCount = null;
  if (poll.run?.id !== undefined && TERMINAL_STATUSES.has(poll.run?.status ?? '')) {
    try {
      const artifacts = await listArtifactsAsync(
        { ...ctx, fetchImpl },
        { projectId, runId: poll.run.id },
      );
      artifactsCount = Array.isArray(artifacts?.items) ? artifacts.items.length : 0;
    } catch {
      artifactsCount = null;
    }
  }

  // 7. Verdict
  const verdict = evaluateVerdict({
    assertThrough,
    run: poll.run,
    sawComputeStart: poll.sawComputeStart,
  });

  const durationMs = nowFn() - start;

  const message = poll.ranOutOfTime
    ? `timed out after ${timeoutMs}ms — run status: ${poll.lastStatus ?? 'never appeared'}`
    : verdict.reason;

  const stage = 'webhook→run';
  const report = buildEnvelope({
    tier: 'e2e-smoke',
    startedAt: startedAtIso,
    durationMs,
    summary: {
      total: 1,
      passed: verdict.outcome === 'pass' ? 1 : 0,
      failed: verdict.outcome === 'pass' ? 0 : 1,
      skipped: 0,
    },
    failures: verdict.outcome === 'pass'
      ? []
      : [{ scenario: 'e2e-smoke', stage, message, artifactPaths: [] }],
  });
  // buildEnvelope defaults `mode: null` for dotnet-tier runs; for the
  // e2e smoke we always report `mode: 'fake'` regardless of verdict
  // (no live model was ever in the loop on this tier — TestFakePi replaces
  // pi entirely, see tests/tools/Comuki.TestFakePi/worker-test.Dockerfile).
  report.mode = 'fake';

  return {
    report,
    run: poll.run,
    sawComputeStart: poll.sawComputeStart,
    ranOutOfTime: poll.ranOutOfTime,
    artifactsCount,
    projectId,
    webhookPath,
  };
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

/**
 * @param {string[]} argv
 * @param {{
 *   stdout?: NodeJS.WritableStream,
 *   stderr?: NodeJS.WritableStream,
 *   env?: NodeJS.ProcessEnv,
 *   fetchImpl?: typeof fetch,
 *   runSmokeFn?: typeof runSmokeAsync,
 *   readBootstrapFn?: typeof readBootstrap,
 *   loginFn?: typeof loginAsync,
 *   stampApiKeyFn?: typeof stampApiKeyAsync,
 *   spawnSyncFn?: typeof spawnSync,
 * }} [deps]
 */
export async function main(argv, deps = {}) {
  const stdout = deps.stdout ?? process.stdout;
  const stderr = deps.stderr ?? process.stderr;
  const env = deps.env ?? process.env;
  const fetchImpl = deps.fetchImpl ?? fetch;

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

  let { baseUrl, apiKey } = options;
  if (typeof baseUrl !== 'string') {
    baseUrl = DEFAULT_BASE_URL;
  }

  // If no apiKey passed, try bootstrap.json, then login + stamp.
  if (typeof apiKey !== 'string' || apiKey.length === 0) {
    const boot = (deps.readBootstrapFn ?? readBootstrap)();
    if (boot !== null) {
      baseUrl = boot.baseUrl;
      apiKey = boot.apiKey;
      stdout.write(`e2e-smoke: using artifacts/e2e/bootstrap.json (baseUrl=${baseUrl})\n`);
    } else {
      stdout.write('e2e-smoke: no --api-key and no bootstrap.json — logging in as bootstrap admin\n');
      try {
        // Lazy import to avoid pulling e2e-up's full body into unit tests
        // that only exercise the smoke flow.
        const upModule = await import('./e2e-up.mjs');
        const adminLogin = await upModule.loginAsync({
          baseUrl,
          email: options.adminEmail,
          password: options.adminPassword,
          fetchImpl,
        });
        const cookieJar = { cookie: '' };
        // Re-issue the login to capture the cookie jar on the side (we
        // need a `get` on /auth/me to be authenticated, not just the
        // login response). The loginAsync above already returned the
        // payload but didn't keep the jar; do it again with our own.
        await upModule.loginAsync({
          baseUrl,
          email: options.adminEmail,
          password: options.adminPassword,
          fetchImpl,
          cookieJar,
        });
        await upModule.fetchMeAsync({ baseUrl, fetchImpl, cookieJar });
        const stamped = await upModule.stampApiKeyAsync({
          baseUrl,
          userId: adminLogin.userId,
          fetchImpl,
          cookieJar,
          label: 'e2e-smoke',
        });
        apiKey = stamped.secret;
      } catch (err) {
        stderr.write(`e2e-smoke: bootstrap login/key failed: ${err.message}\n`);
        return 1;
      }
    }
  }

  const ctx = { baseUrl, apiKey, fetchImpl };

  let result;
  try {
    result = await (deps.runSmokeFn ?? runSmokeAsync)({
      ctx,
      assertThrough: options.assertThrough,
      timeoutMs: options.timeoutMs,
      fetchImpl,
    });
  } catch (err) {
    stderr.write(`e2e-smoke: ${err.message}\n`);
    return 1;
  }

  // Write report.json + report.md
  mkdirSync(options.reportDir, { recursive: true });
  const jsonPath = path.join(options.reportDir, 'report.json');
  const mdPath = path.join(options.reportDir, 'report.md');
  writeFileSync(jsonPath, `${JSON.stringify(result.report, null, 2)}\n`);
  writeFileSync(mdPath, renderMarkdown(result.report));

  stdout.write(`\n${formatVerdict(result.report)}\n`);
  stdout.write(
    `run: ${result.run?.id ?? '(none)'} status=${result.run?.status ?? '(none)'} ` +
      `compute-start-observed=${result.sawComputeStart} ` +
      `artifacts=${result.artifactsCount ?? 'n/a'} ` +
      `out-of-time=${result.ranOutOfTime}\n`,
  );
  stdout.write(`report: ${path.relative(REPO_ROOT, jsonPath)} / ${path.relative(REPO_ROOT, mdPath)}\n`);

  return result.report.summary.failed > 0 ? 1 : 0;
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  main(process.argv.slice(2)).then(
    (code) => { process.exitCode = code; },
    (err) => {
      process.stderr.write(`e2e-smoke: ${err.stack ?? err.message}\n`);
      process.exitCode = 1;
    },
  );
}