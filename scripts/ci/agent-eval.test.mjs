/**
 * Tests for scripts/ci/agent-eval.mjs — `node --test scripts/ci/agent-eval.test.mjs`.
 * Zero dependencies: node:test + node:assert/strict only — same shape as
 * scripts/ci/live-eval.test.mjs.
 *
 * Pure exported functions are tested directly. The CLI entrypoint (`main`)
 * is also exercised, but only with a fake `runDotnet` so no `dotnet`
 * process is ever spawned from these tests, and no real network call is
 * ever possible from this test file.
 */
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  main,
  parseArgs,
  resolveActivation,
  runAgentEval,
} from './agent-eval.mjs';

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

  it('requires --mode when --trend is not passed', () => {
    const result = parseArgs(['--budget-usd=1.00']);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /--mode/);
  });

  it('accepts --trend alone with no --mode', () => {
    const result = parseArgs(['--trend']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.mode, null);
    assert.equal(result.options.trend, true);
  });

  it('rejects an unknown --mode value', () => {
    const result = parseArgs(['--mode=bogus']);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /fake\|replay\|live/);
  });

  it('accepts --mode=fake with defaults', () => {
    const result = parseArgs(['--mode=fake']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.mode, 'fake');
    assert.equal(result.options.corpusPath, null);
    assert.equal(result.options.budgetUsd, '0');
    assert.equal(result.options.trend, false);
  });

  it('accepts --mode=replay and --corpus together', () => {
    const result = parseArgs(['--mode=replay', '--corpus=/tmp/some-corpus']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.mode, 'replay');
    assert.equal(result.options.corpusPath, '/tmp/some-corpus');
  });

  it('accepts --mode=live with --budget-usd', () => {
    const result = parseArgs(['--mode=live', '--budget-usd=5.00']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.mode, 'live');
    assert.equal(result.options.budgetUsd, '5.00');
  });

  it('rejects a negative budget', () => {
    const result = parseArgs(['--mode=fake', '--budget-usd=-1']);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /non-negative/);
  });

  it('rejects a non-numeric budget', () => {
    const result = parseArgs(['--mode=fake', '--budget-usd=not-a-number']);
    assert.equal(result.kind, 'error');
  });

  it('accepts a zero budget explicitly', () => {
    const result = parseArgs(['--mode=fake', '--budget-usd=0']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.budgetUsd, '0');
  });

  it('accepts --mode combined with --trend', () => {
    const result = parseArgs(['--mode=fake', '--trend']);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.mode, 'fake');
    assert.equal(result.options.trend, true);
  });
});

// ---------------------------------------------------------------------------

describe('resolveActivation', () => {
  it('always runs for --trend-only (mode = null)', () => {
    const verdict = resolveActivation({}, null);
    assert.equal(verdict.shouldRun, true);
  });

  it('always runs for --mode=fake regardless of env', () => {
    const verdict = resolveActivation({}, 'fake');
    assert.equal(verdict.shouldRun, true);
  });

  it('always runs for --mode=replay regardless of env', () => {
    const verdict = resolveActivation({}, 'replay');
    assert.equal(verdict.shouldRun, true);
  });

  it('skips --mode=live when COMUKI_LIVE_MODEL_BASE_URL is unset', () => {
    const verdict = resolveActivation({}, 'live');
    assert.equal(verdict.shouldRun, false);
    assert.match(verdict.reason, /COMUKI_LIVE_MODEL_BASE_URL/);
  });

  it('skips --mode=live when COMUKI_LIVE_MODEL_BASE_URL is blank', () => {
    const verdict = resolveActivation({ COMUKI_LIVE_MODEL_BASE_URL: '   ' }, 'live');
    assert.equal(verdict.shouldRun, false);
  });

  it('runs --mode=live when COMUKI_LIVE_MODEL_BASE_URL is set', () => {
    const verdict = resolveActivation({ COMUKI_LIVE_MODEL_BASE_URL: 'http://127.0.0.1:17190' }, 'live');
    assert.equal(verdict.shouldRun, true);
  });
});

// ---------------------------------------------------------------------------

describe('runAgentEval — child-process boundary', () => {
  it('forwards --mode/--corpus/--budget-usd to the injected runDotnet', () => {
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const result = runAgentEval({
      mode: 'fake',
      corpusPath: '/tmp/corpus',
      budgetUsd: '0',
      trend: false,
      projectPath: '/tmp/Comuki.AgentEval.csproj',
      env: { PATH: '/usr/bin' },
      runDotnet,
    });

    assert.equal(result.status, 0);
    assert.equal(calls.length, 1);
    assert.deepEqual(calls[0].args, [
      'run', '--project', '/tmp/Comuki.AgentEval.csproj', '--no-build', '--',
      '--mode=fake', '--corpus=/tmp/corpus', '--budget-usd=0',
    ]);
    assert.equal(calls[0].env.PATH, '/usr/bin');
  });

  it('omits --corpus when not supplied', () => {
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    runAgentEval({
      mode: 'live',
      corpusPath: null,
      budgetUsd: '5.00',
      trend: false,
      projectPath: '/tmp/proj.csproj',
      env: {},
      runDotnet,
    });

    assert.deepEqual(calls[0].args, [
      'run', '--project', '/tmp/proj.csproj', '--no-build', '--',
      '--mode=live', '--budget-usd=5.00',
    ]);
  });

  it('omits --mode and appends --trend for a trend-only call', () => {
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    runAgentEval({
      mode: null,
      corpusPath: null,
      budgetUsd: '0',
      trend: true,
      projectPath: '/tmp/proj.csproj',
      env: {},
      runDotnet,
    });

    assert.deepEqual(calls[0].args, [
      'run', '--project', '/tmp/proj.csproj', '--no-build', '--',
      '--budget-usd=0', '--trend',
    ]);
  });

  it('propagates a non-zero exit code without retrying', () => {
    let callCount = 0;
    const runDotnet = () => {
      callCount += 1;
      return { status: 2, signal: null, stdout: 'fail output', stderr: 'fail error', error: null };
    };

    const result = runAgentEval({
      mode: 'fake',
      corpusPath: null,
      budgetUsd: '0',
      trend: false,
      projectPath: '/tmp/proj.csproj',
      env: {},
      runDotnet,
    });

    assert.equal(result.status, 2);
    assert.equal(callCount, 1);
  });
});

// ---------------------------------------------------------------------------

describe('main — CLI integration (no dotnet, no real network)', () => {
  it('prints usage and returns exit 1 on no args', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const code = main([], { stdout, env: {} });
    assert.equal(code, 1);
    assert.match(out, /agent-eval/);
  });

  it('prints usage and returns exit 0 on --help', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const code = main(['--help'], { stdout, env: {} });
    assert.equal(code, 0);
  });

  it('returns exit 1 with a clear message when --mode is missing and --trend is absent', () => {
    let err = '';
    const stderr = { write: (text) => { err += text; } };
    const code = main(['--budget-usd=1.00'], { stderr, env: {} });
    assert.equal(code, 1);
    assert.match(err, /--mode/);
  });

  it('skips cleanly (exit 0, no dotnet spawn) for --mode=live when COMUKI_LIVE_MODEL_BASE_URL is unset', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    let dotnetCalled = false;
    const runDotnet = () => {
      dotnetCalled = true;
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const code = main(['--mode=live', '--budget-usd=5.00'], { stdout, env: {}, runDotnet });

    assert.equal(code, 0);
    assert.equal(dotnetCalled, false);
    assert.match(out, /skipped/);
  });

  it('never gates --mode=fake on COMUKI_LIVE_MODEL_BASE_URL', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: 'PASS 5/5\n', stderr: '', error: null };
    };

    const code = main(['--mode=fake', '--budget-usd=0'], { stdout, env: {}, runDotnet });

    assert.equal(code, 0);
    assert.equal(calls.length, 1);
    assert.match(out, /PASS 5\/5/);
  });

  it('runs a --trend-only call without requiring --mode', () => {
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: 'timestamp | mode | corpus | pass/total\n', stderr: '', error: null };
    };
    let out = '';
    const stdout = { write: (text) => { out += text; } };

    const code = main(['--trend'], { stdout, env: {}, runDotnet });

    assert.equal(code, 0);
    assert.equal(calls.length, 1);
    assert.ok(calls[0].args.includes('--trend'));
    assert.equal(calls[0].args.includes('--mode='), false);
    assert.match(out, /timestamp \| mode \| corpus/);
  });

  it('surfaces a non-zero dotnet exit code as failure, with stderr tail', () => {
    let err = '';
    const stderr = { write: (text) => { err += text; } };
    const runDotnet = () => ({ status: 1, signal: null, stdout: '', stderr: 'some stderr', error: null });

    const code = main(['--mode=fake', '--budget-usd=0'], { stderr, env: {}, runDotnet });

    assert.equal(code, 1);
    assert.match(err, /exited with status 1/);
  });

  it('surfaces a spawn error as failure', () => {
    let err = '';
    const stderr = { write: (text) => { err += text; } };
    const runDotnet = () => ({ status: null, signal: null, stdout: '', stderr: '', error: new Error('ENOENT: dotnet not found') });

    const code = main(['--mode=fake', '--budget-usd=0'], { stderr, env: {}, runDotnet });

    assert.equal(code, 1);
    assert.match(err, /failed to spawn dotnet/);
  });

  it('defaults to process.env when no env override is supplied (real fallback path, still no network)', () => {
    // No explicit `env` override — main() falls back to process.env. This
    // test process (running under `node --test`) never has
    // COMUKI_LIVE_MODEL_BASE_URL set, so --mode=fake here still never
    // touches the network — it only proves the fallback wiring, via a
    // fake runDotnet.
    let dotnetCalled = false;
    const runDotnet = () => {
      dotnetCalled = true;
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };
    let out = '';
    const stdout = { write: (text) => { out += text; } };

    const code = main(['--mode=fake', '--budget-usd=0'], { stdout, runDotnet });

    assert.equal(code, 0);
    assert.equal(dotnetCalled, true);
  });
});
