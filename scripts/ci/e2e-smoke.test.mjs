/**
 * Tests for scripts/ci/e2e-smoke.mjs — `node --test scripts/ci/e2e-smoke.test.mjs`.
 * Zero deps: node:test + node:assert/strict only. No real network, no real
 * spawn, no real podman. `runSmokeAsync` and the per-step helpers are
 * exercised with a fake `fetchImpl` that returns canned RunDetail-shaped
 * JSON (constructed in-place — never a live API).
 */
import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';

import {
  evaluateVerdict,
  hasComputeStartEvidence,
  loadWebhookFixture,
  main,
  parseArgs,
  pollRunUntilTerminalAsync,
  randomSlugSuffix,
  readBootstrap,
  runSmokeAsync,
  signWebhookPayload,
} from './e2e-smoke.mjs';

let scratch;
before(() => {
  scratch = mkdtempSync(join(tmpdir(), 'e2e-smoke-test-'));
});
after(() => {
  if (scratch !== undefined) {
    rmSync(scratch, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------

describe('parseArgs', () => {
  it('returns usage on --help', () => {
    const result = parseArgs(['--help']);
    assert.equal(result.ok, true);
    assert.equal(result.options.help, true);
  });

  it('accepts --base-url and --api-key', () => {
    const result = parseArgs(['--base-url=http://x:1', '--api-key=ck_abc']);
    assert.equal(result.options.baseUrl, 'http://x:1');
    assert.equal(result.options.apiKey, 'ck_abc');
  });

  it('accepts --assert-through=compute-start', () => {
    const result = parseArgs(['--assert-through=compute-start']);
    assert.equal(result.options.assertThrough, 'compute-start');
  });

  it('rejects --assert-through=bogus', () => {
    const result = parseArgs(['--assert-through=bogus']);
    assert.equal(result.ok, false);
    assert.match(result.error, /--assert-through must be/);
  });

  it('accepts --timeout=600000', () => {
    const result = parseArgs(['--timeout=600000']);
    assert.equal(result.options.timeoutMs, 600000);
  });

  it('rejects non-numeric --timeout', () => {
    const result = parseArgs(['--timeout=forever']);
    assert.equal(result.ok, false);
    assert.match(result.error, /--timeout must be/);
  });

  it('accepts --report-dir (relative)', () => {
    const result = parseArgs(['--report-dir=artifacts/my']);
    // parseArgs resolves relative paths against REPO_ROOT, so the
    // resulting absolute path should end with the supplied relative
    // path (after path-separator normalization on Windows).
    const normalized = result.options.reportDir.replace(/[\\/]/g, '/');
    assert.match(normalized, /artifacts\/my$/);
  });

  it('rejects unknown flags', () => {
    const result = parseArgs(['--nope']);
    assert.equal(result.ok, false);
    assert.match(result.error, /unrecognized/);
  });
});

// ---------------------------------------------------------------------------

describe('signWebhookPayload', () => {
  it('produces sha256=<hex> matching createHmac(\'sha256\', secret)', () => {
    const payload = '{"hello":"world"}';
    const secret = 'the-secret';
    const expected = 'sha256=' + createHmac('sha256', secret).update(payload).digest('hex');
    assert.equal(signWebhookPayload(payload, secret), expected);
  });

  it('uses the default secret when none is passed', () => {
    const payload = 'x';
    const expected = 'sha256=' + createHmac('sha256', 'e2e-smoke-hook-secret').update(payload).digest('hex');
    assert.equal(signWebhookPayload(payload), expected);
  });
});

// ---------------------------------------------------------------------------

describe('randomSlugSuffix', () => {
  it('returns an 8-char string', () => {
    const s = randomSlugSuffix();
    assert.equal(typeof s, 'string');
    assert.equal(s.length, 8);
  });

  it('produces different values across calls', () => {
    const a = randomSlugSuffix();
    const b = randomSlugSuffix();
    assert.notEqual(a, b);
  });
});

// ---------------------------------------------------------------------------

describe('readBootstrap', () => {
  it('returns null when the file does not exist', () => {
    assert.equal(readBootstrap(join(scratch, 'missing.json')), null);
  });

  it('returns null for malformed JSON', () => {
    const file = join(scratch, 'bad.json');
    writeFileSync(file, 'not json{');
    assert.equal(readBootstrap(file), null);
  });

  it('returns null when keys are missing', () => {
    const file = join(scratch, 'partial.json');
    writeFileSync(file, '{"baseUrl":"http://x"}');
    assert.equal(readBootstrap(file), null);
  });

  it('returns { baseUrl, apiKey } on success', () => {
    const file = join(scratch, 'good.json');
    writeFileSync(file, '{"baseUrl":"http://x:1","apiKey":"ck_y","userId":"u","keyId":"k","createdAt":"now"}');
    const result = readBootstrap(file);
    assert.deepEqual(result, { baseUrl: 'http://x:1', apiKey: 'ck_y' });
  });
});

// ---------------------------------------------------------------------------

describe('hasComputeStartEvidence', () => {
  it('returns false on null/undefined', () => {
    assert.equal(hasComputeStartEvidence(null), false);
    assert.equal(hasComputeStartEvidence(undefined), false);
  });

  it('returns false on a run with no events or work items', () => {
    assert.equal(hasComputeStartEvidence({ status: 'queued' }), false);
  });

  it('returns true when any workItem is in running', () => {
    assert.equal(
      hasComputeStartEvidence({ workItems: [{ status: 'queued' }, { status: 'running' }] }),
      true,
    );
  });

  it('returns true when any work_item.status_changed event payload contains "Running"', () => {
    assert.equal(
      hasComputeStartEvidence({
        events: [
          { type: 'run.status_changed', payloadJson: '{"status":"queued"}' },
          { type: 'work_item.status_changed', payloadJson: '{"status":"Running"}' },
        ],
      }),
      true,
    );
  });

  it('ignores non-status_changed events and payloads without "Running"', () => {
    assert.equal(
      hasComputeStartEvidence({
        events: [
          { type: 'worker.reported', payloadJson: '{"running":true}' },
          { type: 'work_item.status_changed', payloadJson: '{"status":"queued"}' },
        ],
      }),
      false,
    );
  });
});

// ---------------------------------------------------------------------------

describe('evaluateVerdict', () => {
  it('terminal+succeeded → pass', () => {
    const v = evaluateVerdict({ assertThrough: 'terminal', run: { status: 'succeeded' }, sawComputeStart: false });
    assert.equal(v.outcome, 'pass');
    assert.match(v.reason, /succeeded/);
  });

  it('terminal+failed → fail', () => {
    const v = evaluateVerdict({ assertThrough: 'terminal', run: { status: 'failed' }, sawComputeStart: true });
    assert.equal(v.outcome, 'fail');
    assert.match(v.reason, /requires 'succeeded'/);
    assert.equal(v.sawComputeStart, true);
  });

  it('terminal+null run → fail', () => {
    const v = evaluateVerdict({ assertThrough: 'terminal', run: null, sawComputeStart: false });
    assert.equal(v.outcome, 'fail');
  });

  it('compute-start+succeeded → pass (succeeded alone is enough)', () => {
    const v = evaluateVerdict({ assertThrough: 'compute-start', run: { status: 'succeeded' }, sawComputeStart: false });
    assert.equal(v.outcome, 'pass');
  });

  it('compute-start+failed+sawComputeStart → pass', () => {
    const v = evaluateVerdict({ assertThrough: 'compute-start', run: { status: 'failed' }, sawComputeStart: true });
    assert.equal(v.outcome, 'pass');
    assert.match(v.reason, /compute-start/);
  });

  it('compute-start+failed+no compute-start evidence → fail', () => {
    const v = evaluateVerdict({ assertThrough: 'compute-start', run: { status: 'failed' }, sawComputeStart: false });
    assert.equal(v.outcome, 'fail');
  });

  it('compute-start+queued+sawComputeStart → pass', () => {
    const v = evaluateVerdict({ assertThrough: 'compute-start', run: { status: 'queued' }, sawComputeStart: true });
    assert.equal(v.outcome, 'pass');
  });
});

// ---------------------------------------------------------------------------

describe('pollRunUntilTerminalAsync', () => {
  function fakeFetch(runsInOrder, runDetail) {
    let listCalls = 0;
    let getCalls = 0;
    const fetchImpl = async (url, init) => {
      const method = (init?.method ?? 'GET').toUpperCase();
      if (method === 'GET' && url.includes('/api/v1/runs?')) {
        const idx = Math.min(listCalls, runsInOrder.length - 1);
        listCalls += 1;
        return new Response(JSON.stringify({ items: [runsInOrder[idx]] }), { status: 200 });
      }
      if (method === 'GET' && url.includes('/api/v1/runs/')) {
        getCalls += 1;
        return new Response(JSON.stringify(runDetail), { status: 200 });
      }
      return new Response('not mocked', { status: 599 });
    };
    return { fetchImpl, calls: () => ({ listCalls, getCalls }) };
  }

  it('returns run when status becomes terminal', async () => {
    const fakeRun = { id: 'r1', projectId: 'p1', status: 'queued' };
    const detail = { id: 'r1', projectId: 'p1', status: 'succeeded', events: [], workItems: [] };
    const { fetchImpl } = fakeFetch([fakeRun], detail);

    const result = await pollRunUntilTerminalAsync({
      ctx: { baseUrl: 'http://x', apiKey: 'k' },
      projectId: 'p1',
      deadlineMs: 5_000,
      intervalMs: 1,
      assertThrough: 'terminal',
      fetchImpl,
      sleepFn: async () => {},
      nowFn: () => Date.now(),
    });

    assert.equal(result.run.status, 'succeeded');
    assert.equal(result.ranOutOfTime, false);
  });

  it('returns ranOutOfTime=true when no run ever appears', async () => {
    const fetchImpl = async (url) => {
      if (url.includes('/api/v1/runs?')) {
        return new Response(JSON.stringify({ items: [] }), { status: 200 });
      }
      return new Response('not mocked', { status: 599 });
    };

    const result = await pollRunUntilTerminalAsync({
      ctx: { baseUrl: 'http://x', apiKey: 'k' },
      projectId: 'p1',
      deadlineMs: 100,
      intervalMs: 5,
      assertThrough: 'terminal',
      fetchImpl,
      sleepFn: async () => {},
      nowFn: (() => {
        let t = 1_000_000;
        return () => t += 10;
      })(),
    });

    assert.equal(result.ranOutOfTime, true);
    assert.equal(result.run, null);
  });

  it('compute-start: short-circuits as soon as evidence is seen, even if status is still queued', async () => {
    const fakeRun = { id: 'r1', projectId: 'p1', status: 'queued' };
    const detail = {
      id: 'r1',
      projectId: 'p1',
      status: 'queued',
      workItems: [{ status: 'running' }],
      events: [],
    };
    const { fetchImpl } = fakeFetch([fakeRun], detail);

    const result = await pollRunUntilTerminalAsync({
      ctx: { baseUrl: 'http://x', apiKey: 'k' },
      projectId: 'p1',
      deadlineMs: 5_000,
      intervalMs: 1,
      assertThrough: 'compute-start',
      fetchImpl,
      sleepFn: async () => {},
      nowFn: () => Date.now(),
    });

    assert.equal(result.sawComputeStart, true);
    assert.equal(result.run.status, 'queued');
    assert.equal(result.ranOutOfTime, false);
  });
});

// ---------------------------------------------------------------------------

describe('loadWebhookFixture', () => {
  it('returns the GitHub issue-opened fixture content from the repo', () => {
    const text = loadWebhookFixture();
    // Sanity-check: the fixture must contain a "comuki" label and the
    // dot-stbl/comuki repo — both used by the smoke scenario.
    assert.match(text, /comuki/);
    assert.match(text, /dot-stbl\/comuki/);
  });
});

// ---------------------------------------------------------------------------

describe('runSmokeAsync — end-to-end with fake fetch', () => {
  function makeFetch({
    projectView = { id: { value: 'p1' } },
    webhookPath = '/api/hooks/github/abc',
    runsBeforeTerminal = [],
    finalRun,
  }) {
    let listIdx = 0;
    const fetchImpl = async (url, init) => {
      const method = (init?.method ?? 'GET').toUpperCase();
      if (method === 'POST' && url.endsWith('/api/v1/projects')) {
        return new Response(JSON.stringify(projectView), { status: 201, headers: { 'content-type': 'application/json' } });
      }
      if (method === 'POST' && url.endsWith('/api/v1/sources')) {
        return new Response(JSON.stringify({ webhookPath }), { status: 201, headers: { 'content-type': 'application/json' } });
      }
      if (method === 'POST' && url.endsWith('/api/v1/admission-rules')) {
        return new Response('{}', { status: 201, headers: { 'content-type': 'application/json' } });
      }
      if (method === 'POST' && url.includes('/api/hooks/github/')) {
        return new Response(JSON.stringify({ outcome: 'admitted' }), { status: 200, headers: { 'content-type': 'application/json' } });
      }
      if (method === 'GET' && url.includes('/api/v1/runs?')) {
        // Progressive simulation: each call returns the next entry from
        // runsBeforeTerminal (or empty once exhausted). Once a non-empty
        // list is returned, the polling loop latches onto runs[0].id and
        // switches to calling getRun() per subsequent loop iteration.
        const items = listIdx < runsBeforeTerminal.length
          ? [runsBeforeTerminal[listIdx]]
          : [];
        listIdx += 1;
        return new Response(JSON.stringify({ items }), { status: 200, headers: { 'content-type': 'application/json' } });
      }
      if (method === 'GET' && url.includes('/artifacts')) {
        // Match artifacts BEFORE the bare /runs/ matcher below — the
        // artifacts URL embeds a runId in its path
        // (`/api/v1/projects/<pid>/runs/<rid>/artifacts`), so a substring
        // match on `/artifacts` is more discriminating than matching
        // /api/v1/runs/ which would also be a substring of it.
        return new Response(JSON.stringify({ items: [], projectId: 'p1', runId: 'r1' }), { status: 200, headers: { 'content-type': 'application/json' } });
      }
      if (method === 'GET' && url.includes('/api/v1/runs/')) {
        return new Response(JSON.stringify(finalRun ?? { id: 'r1', projectId: 'p1', status: 'succeeded', events: [], workItems: [] }), { status: 200, headers: { 'content-type': 'application/json' } });
      }
      return new Response('not mocked', { status: 599 });
    };
    return fetchImpl;
  }

  it('succeeded final run → report summary passes 1/1', async () => {
    const fetchImpl = makeFetch({
      runsBeforeTerminal: [{ id: 'r1', projectId: 'p1', status: 'running' }],
      finalRun: { id: 'r1', projectId: 'p1', status: 'succeeded', events: [], workItems: [] },
    });

    const result = await runSmokeAsync({
      ctx: { baseUrl: 'http://x', apiKey: 'k' },
      assertThrough: 'terminal',
      timeoutMs: 10_000,
      fetchImpl,
      sleepFn: async () => {},
      nowFn: (() => {
        let t = 1_000_000;
        return () => t += 1;
      })(),
      fixturePath: WEBHOOK_FIXTURE_PATH(),
    });

    assert.equal(result.report.summary.passed, 1);
    assert.equal(result.report.summary.failed, 0);
    assert.equal(result.report.tier, 'e2e-smoke');
    assert.equal(result.report.mode, 'fake');
    assert.equal(result.sawComputeStart, false);
    assert.equal(result.ranOutOfTime, false);
  });

  it('compute-start: failed run but with work-item-running evidence → passes', async () => {
    const fetchImpl = makeFetch({
      runsBeforeTerminal: [{ id: 'r1', projectId: 'p1', status: 'running' }],
      finalRun: {
        id: 'r1',
        projectId: 'p1',
        status: 'failed',
        workItems: [{ status: 'running' }, { status: 'failed' }],
        events: [{ type: 'work_item.status_changed', payloadJson: '{"status":"Running"}' }],
      },
    });

    const result = await runSmokeAsync({
      ctx: { baseUrl: 'http://x', apiKey: 'k' },
      assertThrough: 'compute-start',
      timeoutMs: 10_000,
      fetchImpl,
      sleepFn: async () => {},
      nowFn: (() => {
        let t = 1_000_000;
        return () => t += 1;
      })(),
      fixturePath: WEBHOOK_FIXTURE_PATH(),
    });

    assert.equal(result.report.summary.passed, 1);
    assert.equal(result.sawComputeStart, true);
  });

  it('terminal: failed run with no compute-start evidence → fails', async () => {
    const fetchImpl = makeFetch({
      runsBeforeTerminal: [{ id: 'r1', projectId: 'p1', status: 'running' }],
      finalRun: { id: 'r1', projectId: 'p1', status: 'failed', events: [], workItems: [] },
    });

    const result = await runSmokeAsync({
      ctx: { baseUrl: 'http://x', apiKey: 'k' },
      assertThrough: 'terminal',
      timeoutMs: 10_000,
      fetchImpl,
      sleepFn: async () => {},
      nowFn: (() => {
        let t = 1_000_000;
        return () => t += 1;
      })(),
      fixturePath: WEBHOOK_FIXTURE_PATH(),
    });

    assert.equal(result.report.summary.passed, 0);
    assert.equal(result.report.summary.failed, 1);
    assert.equal(result.report.failures.length, 1);
  });

  it('throws when the webhook returns a non-admitted outcome', async () => {
    const fetchImpl = async (url, init) => {
      if (init?.method === 'POST' && url.endsWith('/api/v1/projects')) {
        return new Response(JSON.stringify({ id: { value: 'p1' } }), { status: 201 });
      }
      if (init?.method === 'POST' && url.endsWith('/api/v1/sources')) {
        return new Response(JSON.stringify({ webhookPath: '/api/hooks/github/abc' }), { status: 201 });
      }
      if (init?.method === 'POST' && url.endsWith('/api/v1/admission-rules')) {
        return new Response('{}', { status: 201 });
      }
      if (init?.method === 'POST' && url.includes('/api/hooks/github/')) {
        return new Response(JSON.stringify({ outcome: 'filtered' }), { status: 200 });
      }
      return new Response('not mocked', { status: 599 });
    };

    await assert.rejects(
      runSmokeAsync({
        ctx: { baseUrl: 'http://x', apiKey: 'k' },
        assertThrough: 'terminal',
        timeoutMs: 10_000,
        fetchImpl,
        sleepFn: async () => {},
        nowFn: () => Date.now(),
        fixturePath: WEBHOOK_FIXTURE_PATH(),
      }),
      /outcome is not 'admitted'/,
    );
  });
});

function WEBHOOK_FIXTURE_PATH() {
  // Same default as e2e-smoke.mjs — the repo's committed fixture.
  return join(
    process.cwd(),
    'tests',
    'integration',
    'Comuki.Host.Integration.Intake',
    'Fixtures',
    'github-issue-opened.json',
  );
}

// ---------------------------------------------------------------------------

describe('main — CLI integration (no real network, no real spawn)', () => {
  it('prints usage and returns exit 0 on --help', async () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const code = await main(['--help'], { stdout, env: {} });
    assert.equal(code, 0);
    assert.match(out, /e2e-smoke/);
  });

  it('returns exit 1 on bad args', async () => {
    let err = '';
    const stderr = { write: (text) => { err += text; } };
    const code = await main(['--assert-through=bogus'], { stderr, env: {} });
    assert.equal(code, 1);
    assert.match(err, /--assert-through must be/);
  });

  it('passes when runSmokeAsync returns a passing verdict', async () => {
    const passingReport = {
      report: {
        schemaVersion: 1,
        tier: 'e2e-smoke',
        mode: 'fake',
        startedAt: '2026-09-24T00:00:00.000Z',
        durationMs: 100,
        summary: { total: 1, passed: 1, failed: 0, skipped: 0 },
        failures: [],
        cost: { usdMicros: 0, tokensIn: 0, tokensOut: 0 },
      },
      run: { id: 'r1', status: 'succeeded' },
      sawComputeStart: false,
      ranOutOfTime: false,
      artifactsCount: 0,
      projectId: 'p1',
      webhookPath: '/api/hooks/github/abc',
    };
    const runSmokeFn = async () => passingReport;
    const reportDir = join(scratch, 'reports', 'pass');
    let out = '';
    const stdout = { write: (text) => { out += text; } };

    const code = await main(
      ['--base-url=http://x:1', '--api-key=ck_y', '--report-dir=' + reportDir],
      { stdout, stderr: { write: () => {} }, runSmokeFn, env: {} },
    );

    assert.equal(code, 0);
    assert.match(out, /PASS 1\/1/);
    assert.match(out, /run: r1/);
  });

  it('exits 1 when runSmokeAsync returns a failing verdict', async () => {
    const failingReport = {
      report: {
        schemaVersion: 1,
        tier: 'e2e-smoke',
        mode: 'fake',
        startedAt: '2026-09-24T00:00:00.000Z',
        durationMs: 100,
        summary: { total: 1, passed: 0, failed: 1, skipped: 0 },
        failures: [{ scenario: 'e2e-smoke', stage: 'webhook→run', message: 'failed', artifactPaths: [] }],
        cost: { usdMicros: 0, tokensIn: 0, tokensOut: 0 },
      },
      run: { id: 'r1', status: 'failed' },
      sawComputeStart: false,
      ranOutOfTime: false,
      artifactsCount: null,
      projectId: 'p1',
      webhookPath: '/api/hooks/github/abc',
    };
    const reportDir = join(scratch, 'reports', 'fail');
    let out = '';
    const stdout = { write: (text) => { out += text; } };

    const code = await main(
      ['--base-url=http://x:1', '--api-key=ck_y', '--report-dir=' + reportDir],
      { stdout, stderr: { write: () => {} }, runSmokeFn: async () => failingReport, env: {} },
    );

    assert.equal(code, 1);
    assert.match(out, /FAIL 1\/1/);
  });
});