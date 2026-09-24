/**
 * Tests for scripts/ci/live-eval.mjs — `node --test scripts/ci/live-eval.test.mjs`.
 * Zero dependencies: node:test + node:assert/strict only — same shape as
 * scripts/ci/record-cassette.test.mjs.
 *
 * Pure exported functions are tested directly. The CLI entrypoint (`main`)
 * is also exercised, but only with a fake `runDotnet` so no `dotnet`
 * process is ever spawned from these tests, and no real network call is
 * ever possible from this test file.
 */
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';

import {
  main,
  parseArgs,
  readReportMarkdown,
  resolveActivation,
  runLiveEval,
} from './live-eval.mjs';

let scratch;
before(() => {
  scratch = mkdtempSync(join(tmpdir(), 'live-eval-test-'));
});
after(() => {
  if (scratch !== undefined) {
    rmSync(scratch, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------

describe('parseArgs', () => {
  it('returns usage on no args', () => {
    const result = parseArgs([]);
    assert.equal(result.kind, 'usage');
    assert.equal(result.exitCode, 1);
  });

  it('returns usage (exit 0) on --help', () => {
    const result = parseArgs(['--help']);
    assert.equal(result.kind, 'usage');
    assert.equal(result.exitCode, 0);
  });

  it('requires --budget', () => {
    const result = parseArgs(['--scenario', '/tmp/foo.yaml']);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /--budget/);
  });

  it('accepts --budget=<n> form', () => {
    const result = parseArgs(['--budget=5.00']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.budgetUsd, '5.00');
    assert.equal(result.options.scenarioPath, null);
  });

  it('accepts --budget <n> (space-separated) form', () => {
    const result = parseArgs(['--budget', '2.50']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.budgetUsd, '2.50');
  });

  it('rejects a negative budget', () => {
    const result = parseArgs(['--budget=-1']);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /non-negative/);
  });

  it('rejects a non-numeric budget', () => {
    const result = parseArgs(['--budget=not-a-number']);
    assert.equal(result.kind, 'error');
  });

  it('parses --scenario alongside --budget', () => {
    const result = parseArgs(['--budget=1.00', '--scenario', '/tmp/foo.scenario.yaml']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.scenarioPath, '/tmp/foo.scenario.yaml');
  });

  it('accepts a zero budget', () => {
    const result = parseArgs(['--budget=0']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.budgetUsd, '0');
  });
});

// ---------------------------------------------------------------------------

describe('resolveActivation', () => {
  it('skips when COMUKI_LIVE_MODEL_BASE_URL is unset', () => {
    const verdict = resolveActivation({});
    assert.equal(verdict.shouldRun, false);
    assert.match(verdict.reason, /COMUKI_LIVE_MODEL_BASE_URL/);
  });

  it('skips when COMUKI_LIVE_MODEL_BASE_URL is blank', () => {
    const verdict = resolveActivation({ COMUKI_LIVE_MODEL_BASE_URL: '   ' });
    assert.equal(verdict.shouldRun, false);
  });

  it('runs when COMUKI_LIVE_MODEL_BASE_URL is set', () => {
    const verdict = resolveActivation({ COMUKI_LIVE_MODEL_BASE_URL: 'http://127.0.0.1:17190' });
    assert.equal(verdict.shouldRun, true);
  });
});

// ---------------------------------------------------------------------------

describe('runLiveEval — child-process boundary', () => {
  it('calls the injected runDotnet with the COMUKI_LIVE_* env vars set and the class filter', () => {
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const result = runLiveEval({
      scenarioPath: '/tmp/foo.scenario.yaml',
      budgetUsd: '5.00',
      cassettePath: '/tmp/scratch/cassette.json',
      reportBasePath: '/tmp/scratch/report',
      projectPath: '/tmp/Comuki.EndToEnd.AgentLoop.csproj',
      env: { PATH: '/usr/bin', COMUKI_LIVE_MODEL_BASE_URL: 'http://127.0.0.1:17190' },
      runDotnet,
    });

    assert.equal(result.status, 0);
    assert.equal(calls.length, 1);
    assert.deepEqual(calls[0].args, [
      'run', '--project', '/tmp/Comuki.EndToEnd.AgentLoop.csproj', '--no-build',
      '--', '--filter-class', 'Comuki.EndToEnd.AgentLoop.RealPi.LiveModeScenarioShould',
    ]);
    assert.equal(calls[0].env.COMUKI_LIVE_BUDGET_MAX_USD, '5.00');
    assert.equal(calls[0].env.COMUKI_LIVE_CASSETTE_PATH, '/tmp/scratch/cassette.json');
    assert.equal(calls[0].env.COMUKI_LIVE_REPORT_PATH, '/tmp/scratch/report');
    assert.equal(calls[0].env.COMUKI_LIVE_MODEL_BASE_URL, 'http://127.0.0.1:17190');
    assert.equal(calls[0].env.PATH, '/usr/bin');
  });

  it('passes the token through unmodified when the caller set one', () => {
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    runLiveEval({
      scenarioPath: null,
      budgetUsd: '1.00',
      cassettePath: '/tmp/c.json',
      reportBasePath: '/tmp/r',
      projectPath: '/tmp/proj.csproj',
      env: { COMUKI_LIVE_MODEL_BASE_URL: 'http://127.0.0.1:17190', COMUKI_LIVE_MODEL_TOKEN: 'secret-token' },
      runDotnet,
    });

    assert.equal(calls[0].env.COMUKI_LIVE_MODEL_TOKEN, 'secret-token');
  });

  it('propagates a non-zero exit code without retrying', () => {
    let callCount = 0;
    const runDotnet = () => {
      callCount += 1;
      return { status: 2, signal: null, stdout: 'fail output', stderr: 'fail error', error: null };
    };

    const result = runLiveEval({
      scenarioPath: null,
      budgetUsd: '1.00',
      cassettePath: '/tmp/c.json',
      reportBasePath: '/tmp/r',
      projectPath: '/tmp/proj.csproj',
      env: {},
      runDotnet,
    });

    assert.equal(result.status, 2);
    assert.equal(callCount, 1);
  });
});

// ---------------------------------------------------------------------------

describe('readReportMarkdown', () => {
  it('reads the markdown file at <basePath>.md when it exists', () => {
    const basePath = join(scratch, 'a-report');
    writeFileSync(`${basePath}.md`, '# agent-loop report\n\nPASS 1/1\n');

    const markdown = readReportMarkdown(basePath);
    assert.match(markdown, /PASS 1\/1/);
  });

  it('returns null when the file does not exist', () => {
    const markdown = readReportMarkdown(join(scratch, 'missing-report'));
    assert.equal(markdown, null);
  });
});

// ---------------------------------------------------------------------------

describe('main — CLI integration (no dotnet, no real network)', () => {
  it('prints usage and returns exit 1 on no args', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const code = main([], { stdout, env: {} });
    assert.equal(code, 1);
    assert.match(out, /live-eval/);
  });

  it('prints usage and returns exit 0 on --help', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const code = main(['--help'], { stdout, env: {} });
    assert.equal(code, 0);
  });

  it('returns exit 1 with a clear message when --budget is missing', () => {
    let err = '';
    const stderr = { write: (text) => { err += text; } };
    const code = main(['--scenario', '/tmp/foo.yaml'], { stderr, env: {} });
    assert.equal(code, 1);
    assert.match(err, /--budget/);
  });

  it('skips cleanly (exit 0, no dotnet spawn) when COMUKI_LIVE_MODEL_BASE_URL is unset', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    let dotnetCalled = false;
    const runDotnet = () => {
      dotnetCalled = true;
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const code = main(['--budget=5.00'], { stdout, env: {}, runDotnet });

    assert.equal(code, 0);
    assert.equal(dotnetCalled, false);
    assert.match(out, /skipped/);
  });

  it('skips cleanly even when the real process.env has no live base url (default env path)', () => {
    // No explicit `env` override — main() falls back to process.env, and this
    // test process (running under `node --test`) never has COMUKI_LIVE_MODEL_BASE_URL
    // set, so this exercises the real fallback path without touching the network.
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    let dotnetCalled = false;
    const runDotnet = () => {
      dotnetCalled = true;
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const code = main(['--budget=1.00'], { stdout, runDotnet });

    assert.equal(code, 0);
    assert.equal(dotnetCalled, false);
    assert.match(out, /skipped/);
  });

  it('never logs the token value even when one is set', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const runDotnet = () => ({ status: 0, signal: null, stdout: '', stderr: '', error: null });

    const code = main(['--budget=1.00'], {
      stdout,
      runDotnet,
      env: { COMUKI_LIVE_MODEL_BASE_URL: 'http://127.0.0.1:17190', COMUKI_LIVE_MODEL_TOKEN: 'super-secret-value' },
      scratchDir: scratch,
    });

    assert.equal(code, 0);
    assert.doesNotMatch(out, /super-secret-value/);
    assert.match(out, /not shown/);
  });

  it('drives dotnet with a class filter and reports PASS when the run succeeds and a report exists', () => {
    const reportBase = join(scratch, 'live-eval-report');
    writeFileSync(`${reportBase}.md`, '# agent-loop report\n\nPASS 1/1\n');

    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const code = main(['--budget=3.00'], {
      stdout,
      runDotnet,
      env: { COMUKI_LIVE_MODEL_BASE_URL: 'http://127.0.0.1:17190' },
      scratchDir: scratch,
      readFileSync: (path, encoding) => {
        if (path === `${reportBase}.md`) {
          return '# agent-loop report\n\nPASS 1/1\n';
        }
        throw new Error(`unexpected read: ${path}`);
      },
    });

    assert.equal(code, 0);
    assert.equal(calls.length, 1);
    assert.match(out, /PASS/);
  });

  it('surfaces a non-zero dotnet exit code as failure, with stdout/stderr tail when no report exists', () => {
    let out = '';
    let err = '';
    const stdout = { write: (text) => { out += text; } };
    const stderr = { write: (text) => { err += text; } };
    const runDotnet = () => ({ status: 1, signal: null, stdout: 'some stdout', stderr: 'some stderr', error: null });

    const code = main(['--budget=1.00'], {
      stdout,
      stderr,
      runDotnet,
      env: { COMUKI_LIVE_MODEL_BASE_URL: 'http://127.0.0.1:17190' },
      scratchDir: scratch,
    });

    assert.equal(code, 1);
    assert.match(err, /exited with status 1/);
  });

  it('surfaces a spawn error as failure', () => {
    let out = '';
    let err = '';
    const stdout = { write: (text) => { out += text; } };
    const stderr = { write: (text) => { err += text; } };
    const runDotnet = () => ({ status: null, signal: null, stdout: '', stderr: '', error: new Error('ENOENT: dotnet not found') });

    const code = main(['--budget=1.00'], {
      stdout,
      stderr,
      runDotnet,
      env: { COMUKI_LIVE_MODEL_BASE_URL: 'http://127.0.0.1:17190' },
      scratchDir: scratch,
    });

    assert.equal(code, 1);
    assert.match(err, /failed to spawn dotnet/);
  });
});
