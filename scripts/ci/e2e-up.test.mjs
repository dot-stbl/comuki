/**
 * Tests for scripts/ci/e2e-up.mjs — `node --test scripts/ci/e2e-up.test.mjs`.
 * Zero deps: node:test + node:assert/strict only. No real `podman` /
 * `docker` invocations, no real network calls.
 */
import assert from 'node:assert/strict';
import { after, before, describe, it } from 'node:test';

import {
  buildTestFakeModel,
  composeUp,
  detectComposeCmd,
  dumpServiceLogs,
  main,
  parseArgs,
} from './e2e-up.mjs';

let capturedSpawnCalls;

function recordingSpawn() {
  capturedSpawnCalls = [];
  return (cmd, args, options) => {
    capturedSpawnCalls.push({ cmd, args, options });
    return { status: 0, signal: null, stdout: '', stderr: '', error: null };
  };
}

before(() => {
  capturedSpawnCalls = [];
});
after(() => {});

// ---------------------------------------------------------------------------

describe('parseArgs', () => {
  it('returns usage (exit 0) on --help', () => {
    const result = parseArgs(['--help']);
    assert.equal(result.ok, true);
    assert.equal(result.options.help, true);
  });

  it('returns usage (exit 1) on no args', () => {
    const result = parseArgs([]);
    assert.equal(result.ok, true);
    assert.equal(result.options.help, false);
    // main() handles the empty case; parseArgs just leaves defaults.
  });

  it('accepts --skip-build', () => {
    const result = parseArgs(['--skip-build']);
    assert.equal(result.options.skipBuild, true);
  });

  it('accepts --compose-cmd=docker', () => {
    const result = parseArgs(['--compose-cmd=docker']);
    assert.equal(result.options.composeCmd, 'docker');
  });

  it('accepts --compose-cmd podman (space-separated)', () => {
    const result = parseArgs(['--compose-cmd', 'podman']);
    assert.equal(result.options.composeCmd, 'podman');
  });

  it('rejects invalid --compose-cmd', () => {
    const result = parseArgs(['--compose-cmd=nerdctl']);
    assert.equal(result.ok, false);
    assert.match(result.error, /--compose-cmd must be auto\|podman\|docker/);
  });

  it('accepts --timeout=300000', () => {
    const result = parseArgs(['--timeout=300000']);
    assert.equal(result.options.timeoutMs, 300000);
  });

  it('rejects non-numeric --timeout', () => {
    const result = parseArgs(['--timeout=forever']);
    assert.equal(result.ok, false);
    assert.match(result.error, /--timeout must be/);
  });

  it('accepts --base-url and --admin-* overrides', () => {
    const result = parseArgs([
      '--base-url=http://example.test:9999',
      '--admin-email=a@b.test',
      '--admin-password=pwpwpw',
    ]);
    assert.equal(result.options.baseUrl, 'http://example.test:9999');
    assert.equal(result.options.adminEmail, 'a@b.test');
    assert.equal(result.options.adminPassword, 'pwpwpw');
  });

  it('rejects unknown flags', () => {
    const result = parseArgs(['--nope']);
    assert.equal(result.ok, false);
    assert.match(result.error, /unrecognized/);
  });
});

// ---------------------------------------------------------------------------

describe('detectComposeCmd', () => {
  it('returns "podman" when podman compose version exits 0', () => {
    const run = (cmd, _args) => {
      if (cmd === 'podman') {
        return { status: 0, signal: null, stdout: '', stderr: '', error: null };
      }
      return { status: 1, signal: null, stdout: '', stderr: '', error: new Error('not found') };
    };
    assert.equal(detectComposeCmd({ run, env: {} }), 'podman');
  });

  it('falls back to "docker" when podman is absent', () => {
    const run = (cmd, _args) => {
      if (cmd === 'docker') {
        return { status: 0, signal: null, stdout: '', stderr: '', error: null };
      }
      return { status: 1, signal: null, stdout: '', stderr: '', error: new Error('not found') };
    };
    assert.equal(detectComposeCmd({ run, env: {} }), 'docker');
  });

  it('throws when neither podman nor docker works', () => {
    const run = () => ({ status: 1, signal: null, stdout: '', stderr: '', error: new Error('ENOTFOUND') });
    assert.throws(() => detectComposeCmd({ run, env: {} }), /no compose-capable runtime/);
  });
});

// ---------------------------------------------------------------------------

describe('buildTestFakeModel', () => {
  it('invokes podman build with the --ignorefile flag and the right dockerfile', () => {
    const calls = [];
    const run = (cmd, args, options) => {
      calls.push({ cmd, args, options });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const result = buildTestFakeModel({
      run,
      cwd: '/tmp/repo',
      dockerfile: '/tmp/repo/tests/tools/Comuki.TestFakeModel/Dockerfile',
      ignorefile: '/tmp/repo/tests/tools/Comuki.TestFakeModel/.containerignore',
      image: 'comuki-test-fake-model:e2e',
    });

    assert.equal(result.status, 0);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].cmd, 'podman');
    assert.deepEqual(calls[0].args, [
      'build',
      '--ignorefile', '/tmp/repo/tests/tools/Comuki.TestFakeModel/.containerignore',
      '-f', '/tmp/repo/tests/tools/Comuki.TestFakeModel/Dockerfile',
      '-t', 'comuki-test-fake-model:e2e',
      '.',
    ]);
    assert.equal(calls[0].options.cwd, '/tmp/repo');
  });

  it('propagates a non-zero exit', () => {
    const run = () => ({ status: 17, signal: null, stdout: '', stderr: 'dockerfile parse error', error: null });
    const result = buildTestFakeModel({ run });
    assert.equal(result.status, 17);
    assert.match(result.stderr, /dockerfile parse error/);
  });
});

// ---------------------------------------------------------------------------

describe('composeUp', () => {
  it('runs <cmd> compose -f <file> up -d --wait --wait-timeout <n>', () => {
    const calls = [];
    const run = (cmd, args, options) => {
      calls.push({ cmd, args, options });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    composeUp({ cmd: 'podman', run, file: '/tmp/compose.e2e.yml', waitTimeoutSeconds: 600 });

    assert.equal(calls.length, 1);
    assert.equal(calls[0].cmd, 'podman');
    assert.deepEqual(calls[0].args, [
      'compose',
      '-f', '/tmp/compose.e2e.yml',
      'up',
      '-d',
      '--wait',
      '--wait-timeout', '600',
    ]);
  });
});

// ---------------------------------------------------------------------------

describe('dumpServiceLogs', () => {
  it('runs <cmd> compose -f <file> logs --tail=200 and writes both streams to stderr', () => {
    const calls = [];
    const run = (cmd, args, options) => {
      calls.push({ cmd, args, options });
      return {
        status: 0,
        signal: null,
        stdout: 'service-a log\n',
        stderr: 'service-b log\n',
        error: null,
      };
    };

    let sink = '';
    const stderr = { write: (text) => { sink += text; } };
    dumpServiceLogs({ cmd: 'podman', run, file: '/tmp/c.yml', stderr });

    assert.equal(calls.length, 1);
    assert.deepEqual(calls[0].args.slice(0, 3), ['compose', '-f', '/tmp/c.yml']);
    assert.match(calls[0].args[3], /^logs$/);
    assert.match(sink, /service-a log/);
    assert.match(sink, /service-b log/);
    assert.match(sink, /compose logs/);
  });
});

// ---------------------------------------------------------------------------

describe('main — CLI integration (no real podman, no real network)', () => {
  it('prints usage and returns exit 0 on --help', async () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const code = await main(['--help'], { stdout, env: {} });
    assert.equal(code, 0);
    assert.match(out, /e2e-up/);
  });

  it('returns exit 1 on bad args', async () => {
    let err = '';
    const stderr = { write: (text) => { err += text; } };
    const code = await main(['--compose-cmd=nerdctl'], { stderr, env: {} });
    assert.equal(code, 1);
    assert.match(err, /--compose-cmd must be/);
  });

  it('with --skip-build and a fake compose, reaches the health-probe stage', async () => {
    let probeCalls = [];
    const run = (cmd, args, options) => {
      probeCalls.push({ cmd, args });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    // Health returns 200 immediately; login + key stamp return canned values.
    const fetchCalls = [];
    const fetchImpl = async (url, init) => {
      fetchCalls.push({ url, init });
      if (url.endsWith('/api/v1/health')) {
        return new Response(JSON.stringify({ status: 'ok' }), { status: 200, headers: { 'content-type': 'application/json' } });
      }
      if (url.endsWith('/api/v1/auth/login')) {
        return new Response(JSON.stringify({ userId: '00000000-0000-0000-0000-000000000001', email: 'a@b.c', displayName: 'Admin' }), {
          status: 200,
          headers: { 'content-type': 'application/json', 'set-cookie': 'sid=abc123; Path=/' },
        });
      }
      if (url.endsWith('/api/v1/auth/me')) {
        return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } });
      }
      if (url.endsWith('/api/v1/keys')) {
        return new Response(JSON.stringify({ keyId: '00000000-0000-0000-0000-000000000002', prefix: 'ck_e2e', secret: 'ck_fake_secret' }), {
          status: 201,
          headers: { 'content-type': 'application/json' },
        });
      }
      return new Response('not mocked', { status: 599 });
    };

    let stdoutText = '';
    let stderrText = '';
    const stdout = { write: (text) => { stdoutText += text; } };
    const stderr = { write: (text) => { stderrText += text; } };

    const code = await main(['--skip-build', '--compose-cmd=podman'], {
      run,
      fetchImpl,
      stdout,
      stderr,
      env: {},
    });

    assert.equal(code, 0, `stderr was: ${stderrText}`);
    assert.match(stdoutText, /stack is up/);
    assert.equal(probeCalls.length, 1); // just compose up (no build, no logs)
    assert.equal(probeCalls[0].cmd, 'podman');
    // args = ['compose', '-f', <file>, 'up', '-d', '--wait', '--wait-timeout', '600']
    assert.match(probeCalls[0].args[2], /compose\.e2e\.yml/);
    assert.ok(fetchCalls.some((c) => c.url.endsWith('/api/v1/health')));
    assert.ok(fetchCalls.some((c) => c.url.endsWith('/api/v1/auth/login')));
    assert.ok(fetchCalls.some((c) => c.url.endsWith('/api/v1/keys')));
  });

  it('dumps service logs and returns 1 when compose up fails', async () => {
    // Differentiate the mock by args[3] (the compose subcommand — 'up' vs
    // 'logs'): the compose-up call returns the failure body, the
    // dumpServiceLogs call returns the service log line content. Without
    // this split both calls would return the same failure body and the
    // assertion would never see 'service log line'.
    // args = ['compose', '-f', <file>, <subcommand>, ...]
    const run = (cmd, args) => {
      if (cmd !== 'podman') {
        return { status: 1, signal: null, stdout: '', stderr: '', error: new Error('unexpected cmd') };
      }
      if (args?.[3] === 'up') {
        return { status: 1, signal: null, stdout: 'bringup failed', stderr: 'compose stderr', error: null };
      }
      if (args?.[3] === 'logs') {
        return { status: 0, signal: null, stdout: 'service log line\n', stderr: '', error: null };
      }
      return { status: 1, signal: null, stdout: '', stderr: '', error: new Error(`unexpected args: ${JSON.stringify(args)}`) };
    };

    let stderrText = '';
    const stderr = { write: (text) => { stderrText += text; } };

    const code = await main(['--skip-build', '--compose-cmd=podman'], {
      run,
      fetchImpl: async () => new Response('', { status: 200 }),
      stdout: { write: () => {} },
      stderr,
      env: {},
    });

    assert.equal(code, 1);
    assert.match(stderrText, /compose up exited 1/);
    assert.match(stderrText, /service log line/);
  });

  it('returns 1 when detectComposeCmd throws (no runtime installed)', async () => {
    const run = () => ({ status: 1, signal: null, stdout: '', stderr: '', error: new Error('not found') });
    let stderrText = '';
    const stderr = { write: (text) => { stderrText += text; } };
    const code = await main([], {
      run,
      fetchImpl: async () => new Response('', { status: 200 }),
      stdout: { write: () => {} },
      stderr,
      env: {},
    });
    assert.equal(code, 1);
    assert.match(stderrText, /no compose-capable runtime/);
  });
});

// ---------------------------------------------------------------------------

describe('buildTestFakeModel + composeUp + detect (smoke shape)', () => {
  it('chain in correct order under --skip-build=false', async () => {
    const calls = [];
    const run = (cmd, args) => {
      calls.push({ stage: 'spawn', cmd, head: args[0] });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    let fetchImpl = async () => {
      if (Math.random() < 0) {
        // unreachable
      }
      return new Response('{}', { status: 200 });
    };
    fetchImpl = async (url) => {
      calls.push({ stage: 'fetch', url });
      if (url.endsWith('/api/v1/health')) {
        return new Response(JSON.stringify({ status: 'ok' }), { status: 200 });
      }
      if (url.endsWith('/api/v1/auth/login')) {
        return new Response(JSON.stringify({ userId: '00000000-0000-0000-0000-000000000001', email: 'a', displayName: 'A' }), {
          status: 200,
          headers: { 'set-cookie': 'sid=1' },
        });
      }
      if (url.endsWith('/api/v1/auth/me')) {
        return new Response('{}', { status: 200 });
      }
      if (url.endsWith('/api/v1/keys')) {
        return new Response(JSON.stringify({ keyId: '00000000-0000-0000-0000-000000000002', prefix: 'k', secret: 's' }), {
          status: 201,
        });
      }
      return new Response('not mocked', { status: 599 });
    };

    await main(['--compose-cmd=podman'], {
      run,
      fetchImpl,
      stdout: { write: () => {} },
      stderr: { write: () => {} },
      env: {},
    });

    // First spawn call: podman build; second: podman compose up.
    const spawns = calls.filter((c) => c.stage === 'spawn');
    assert.equal(spawns.length, 2);
    assert.equal(spawns[0].cmd, 'podman');
    assert.equal(spawns[0].head, 'build');
    assert.equal(spawns[1].cmd, 'podman');
    assert.equal(spawns[1].head, 'compose');

    const fetchCalls = calls.filter((c) => c.stage === 'fetch').map((c) => c.url);
    // health probe runs first
    assert.ok(fetchCalls.some((u) => u.endsWith('/api/v1/health')));
  });
});