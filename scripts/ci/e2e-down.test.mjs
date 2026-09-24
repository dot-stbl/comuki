/**
 * Tests for scripts/ci/e2e-down.mjs — `node --test scripts/ci/e2e-down.test.mjs`.
 * Zero deps: node:test + node:assert/strict only. No real podman/docker.
 */
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';

import {
  composeDown,
  detectComposeCmd,
  main,
  parseArgs,
  removeBootstrapFile,
} from './e2e-down.mjs';

let scratch;
before(() => {
  scratch = mkdtempSync(join(tmpdir(), 'e2e-down-test-'));
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

  it('accepts --compose-cmd=docker', () => {
    const result = parseArgs(['--compose-cmd=docker']);
    assert.equal(result.options.composeCmd, 'docker');
  });

  it('rejects --compose-cmd=nerdctl', () => {
    const result = parseArgs(['--compose-cmd=nerdctl']);
    assert.equal(result.ok, false);
  });

  it('accepts --timeout=180000', () => {
    const result = parseArgs(['--timeout=180000']);
    assert.equal(result.options.timeoutMs, 180000);
  });

  it('rejects non-numeric --timeout', () => {
    const result = parseArgs(['--timeout=abc']);
    assert.equal(result.ok, false);
    assert.match(result.error, /--timeout must be/);
  });

  it('accepts --keep-bootstrap', () => {
    const result = parseArgs(['--keep-bootstrap']);
    assert.equal(result.options.keepBootstrap, true);
  });

  it('rejects unknown flags', () => {
    const result = parseArgs(['--wat']);
    assert.equal(result.ok, false);
    assert.match(result.error, /unrecognized/);
  });
});

// ---------------------------------------------------------------------------

describe('composeDown', () => {
  it('runs <cmd> compose -f <file> down -v --remove-orphans', () => {
    const calls = [];
    const run = (cmd, args) => {
      calls.push({ cmd, args });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    composeDown({ cmd: 'podman', run, file: '/tmp/c.yml' });

    assert.equal(calls.length, 1);
    assert.equal(calls[0].cmd, 'podman');
    assert.deepEqual(calls[0].args, [
      'compose',
      '-f', '/tmp/c.yml',
      'down',
      '-v',
      '--remove-orphans',
    ]);
  });

  it('propagates the spawn error', () => {
    const run = () => ({ status: null, signal: null, stdout: '', stderr: '', error: new Error('ENOENT') });
    const result = composeDown({ cmd: 'podman', run });
    assert.ok(result.error);
    assert.match(result.error.message, /ENOENT/);
  });
});

// ---------------------------------------------------------------------------

describe('detectComposeCmd', () => {
  it('returns "podman" when it works', () => {
    const run = (cmd) => cmd === 'podman'
      ? { status: 0, signal: null, stdout: '', stderr: '', error: null }
      : { status: 1, signal: null, stdout: '', stderr: '', error: new Error('x') };
    assert.equal(detectComposeCmd({ run, env: {} }), 'podman');
  });

  it('falls back to "docker"', () => {
    const run = (cmd) => cmd === 'docker'
      ? { status: 0, signal: null, stdout: '', stderr: '', error: null }
      : { status: 1, signal: null, stdout: '', stderr: '', error: new Error('x') };
    assert.equal(detectComposeCmd({ run, env: {} }), 'docker');
  });

  it('throws when neither works', () => {
    const run = () => ({ status: 1, signal: null, stdout: '', stderr: '', error: new Error('x') });
    assert.throws(() => detectComposeCmd({ run, env: {} }), /no compose-capable runtime/);
  });
});

// ---------------------------------------------------------------------------

describe('removeBootstrapFile', () => {
  it('returns false when the file does not exist', () => {
    const removed = removeBootstrapFile(join(scratch, 'does-not-exist.json'));
    assert.equal(removed, false);
  });

  it('removes an existing file and returns true', () => {
    const file = join(scratch, 'bootstrap.json');
    writeFileSync(file, '{}');
    assert.ok(existsSync(file));
    const removed = removeBootstrapFile(file);
    assert.equal(removed, true);
    assert.equal(existsSync(file), false);
  });

  it('swallows errors and returns false', () => {
    // Point at a path whose parent doesn't exist; rmSync would throw ENOENT.
    const file = join(scratch, 'missing-dir', 'bootstrap.json');
    const removed = removeBootstrapFile(file);
    assert.equal(removed, false);
  });
});

// ---------------------------------------------------------------------------

describe('main — CLI integration (no real podman)', () => {
  it('prints usage and returns exit 0 on --help', () => {
    let out = '';
    const stdout = { write: (text) => { out += text; } };
    const code = main(['--help'], { stdout, env: {} });
    assert.equal(code, 0);
    assert.match(out, /e2e-down/);
  });

  it('returns exit 1 on bad args', () => {
    let err = '';
    const stderr = { write: (text) => { err += text; } };
    const code = main(['--compose-cmd=nerdctl'], { stderr, env: {} });
    assert.equal(code, 1);
    assert.match(err, /--compose-cmd must be/);
  });

  it('runs compose down, removes bootstrap.json, exits 0', () => {
    const bootstrap = join(scratch, 'bootstrap.json');
    writeFileSync(bootstrap, '{"baseUrl":"http://localhost:17180","apiKey":"ck_x"}');
    assert.ok(existsSync(bootstrap));

    const calls = [];
    const run = (cmd, args) => {
      calls.push({ cmd, args });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    let out = '';
    const stdout = { write: (text) => { out += text; } };

    const code = main(['--compose-cmd=podman'], {
      run,
      stdout,
      stderr: { write: () => {} },
      env: {},
      removeBootstrapFn: () => removeBootstrapFile(bootstrap),
    });

    assert.equal(code, 0);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].cmd, 'podman');
    // args = ['compose', '-f', <file>, 'down', '-v', '--remove-orphans']
    assert.match(calls[0].args[2], /compose\.e2e\.yml/);
    assert.match(out, /stack torn down/);
    assert.equal(existsSync(bootstrap), false);
  });

  it('does NOT remove bootstrap.json when --keep-bootstrap is passed', () => {
    const bootstrap = join(scratch, 'bootstrap.json');
    writeFileSync(bootstrap, '{}');

    let removeCalls = 0;
    const run = () => ({ status: 0, signal: null, stdout: '', stderr: '', error: null });
    const code = main(['--compose-cmd=podman', '--keep-bootstrap'], {
      run,
      stdout: { write: () => {} },
      stderr: { write: () => {} },
      env: {},
      removeBootstrapFn: () => { removeCalls += 1; return false; },
    });

    assert.equal(code, 0);
    assert.equal(removeCalls, 0);
    assert.ok(existsSync(bootstrap));
  });

  it('propagates a non-zero compose down exit code', () => {
    const run = () => ({ status: 2, signal: null, stdout: '', stderr: '', error: null });
    let err = '';
    const stderr = { write: (text) => { err += text; } };
    const code = main(['--compose-cmd=podman'], {
      run,
      stdout: { write: () => {} },
      stderr,
      env: {},
    });
    assert.equal(code, 2);
    assert.match(err, /compose down exited 2/);
  });

  it('throws when detectComposeCmd throws (no runtime)', () => {
    // The script does NOT swallow detectComposeCmd's "no runtime" error —
    // it lets it propagate out of main(), where the entry-point guard
    // turns it into a non-zero exit code. We assert the throw here so
    // a future refactor that silently swallowed it would be caught.
    const run = () => ({ status: 1, signal: null, stdout: '', stderr: '', error: new Error('x') });
    assert.throws(
      () => main([], {
        run,
        stdout: { write: () => {} },
        stderr: { write: () => {} },
        env: {},
      }),
      /no compose-capable runtime/,
    );
  });
});