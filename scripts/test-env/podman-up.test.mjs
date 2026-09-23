/**
 * Tests for scripts/test-env/podman-up.mjs — `node --test scripts/test-env/podman-up.test.mjs`.
 * Zero dependencies: node:test + node:assert/strict only. Covers the pure
 * contract functions (pipe/socket <-> DOCKER_HOST conversion, machine
 * selection, export formatting, raw HTTP response parsing) — the podman CLI
 * calls and the live socket ping are exercised manually, not here (see
 * .agents/rules/process/local-test-runtime.md for the validated recipe).
 */
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  PodmanRuntimeError,
  dockerHostToConnectTarget,
  formatExports,
  parseHttpResponse,
  pipePathToDockerHost,
  posixSocketToDockerHost,
  selectMachine,
} from './podman-up.mjs';

describe('PodmanRuntimeError', () => {
  it('is a real Error subclass with a stable name', () => {
    const error = new PodmanRuntimeError('no Podman machine found');
    assert.ok(error instanceof Error);
    assert.equal(error.name, 'PodmanRuntimeError');
    assert.equal(error.message, 'no Podman machine found');
  });

  it('is what every detection/configuration failure throws', () => {
    assert.throws(() => selectMachine([], undefined), PodmanRuntimeError);
    assert.throws(() => pipePathToDockerHost('/wrong/shape'), PodmanRuntimeError);
    assert.throws(() => dockerHostToConnectTarget('ssh://example.com'), PodmanRuntimeError);
  });
});

describe('pipePathToDockerHost', () => {
  it('converts the default machine pipe to the two-slash npipe form Testcontainers .NET accepts', () => {
    assert.equal(
      pipePathToDockerHost('\\\\.\\pipe\\podman-machine-default'),
      'npipe://./pipe/podman-machine-default',
    );
  });

  it('carries through a non-default machine name', () => {
    assert.equal(pipePathToDockerHost('\\\\.\\pipe\\podman-machine-work'), 'npipe://./pipe/podman-machine-work');
  });

  it('rejects a path that is not a Windows named pipe', () => {
    assert.throws(() => pipePathToDockerHost('/run/user/1000/podman/podman.sock'), /unrecognized Windows named pipe/);
  });
});

describe('posixSocketToDockerHost', () => {
  it('prefixes a unix socket path with unix://', () => {
    assert.equal(posixSocketToDockerHost('/run/user/1000/podman/podman.sock'), 'unix:///run/user/1000/podman/podman.sock');
  });
});

describe('dockerHostToConnectTarget', () => {
  it('reads the two-slash npipe form (what this repo actually uses)', () => {
    assert.deepEqual(dockerHostToConnectTarget('npipe://./pipe/podman-machine-default'), {
      kind: 'npipe',
      path: '\\\\.\\pipe\\podman-machine-default',
    });
  });

  it('also reads the four-slash Docker-Desktop form, for a pasted-in value', () => {
    assert.deepEqual(dockerHostToConnectTarget('npipe:////./pipe/podman-machine-default'), {
      kind: 'npipe',
      path: '\\\\.\\pipe\\podman-machine-default',
    });
  });

  it('reads a unix socket', () => {
    assert.deepEqual(dockerHostToConnectTarget('unix:///run/user/1000/podman/podman.sock'), {
      kind: 'unix',
      path: '/run/user/1000/podman/podman.sock',
    });
  });

  it('reads a tcp endpoint with an explicit port', () => {
    assert.deepEqual(dockerHostToConnectTarget('tcp://127.0.0.1:2375'), {
      kind: 'tcp',
      host: '127.0.0.1',
      port: 2375,
    });
  });

  it('defaults the tcp port to 2375 when absent', () => {
    assert.deepEqual(dockerHostToConnectTarget('tcp://localhost'), { kind: 'tcp', host: 'localhost', port: 2375 });
  });

  it('rejects an unsupported scheme', () => {
    assert.throws(() => dockerHostToConnectTarget('ssh://example.com'), /unsupported DOCKER_HOST scheme/);
  });

  it('rejects an npipe value with no pipe name', () => {
    assert.throws(() => dockerHostToConnectTarget('npipe://./nope/x'), /cannot find a pipe name/);
  });
});

describe('selectMachine', () => {
  const twoMachines = [
    { Name: 'podman-machine-default', Default: false, Running: true },
    { Name: 'podman-machine-work', Default: true, Running: false },
  ];

  it('picks the machine marked Default', () => {
    assert.equal(selectMachine(twoMachines, undefined).Name, 'podman-machine-work');
  });

  it('picks the only machine when there is exactly one, default flag or not', () => {
    const single = [{ Name: 'podman-machine-default', Default: false, Running: true }];
    assert.equal(selectMachine(single, undefined).Name, 'podman-machine-default');
  });

  it('falls back to the conventional name when several machines exist and none is marked default', () => {
    const noDefault = [
      { Name: 'podman-machine-default', Default: false, Running: true },
      { Name: 'podman-machine-work', Default: false, Running: false },
    ];
    assert.equal(selectMachine(noDefault, undefined).Name, 'podman-machine-default');
  });

  it('throws on ambiguity — several machines, none default, none conventionally named', () => {
    const ambiguous = [
      { Name: 'alpha', Default: false, Running: true },
      { Name: 'beta', Default: false, Running: false },
    ];
    assert.throws(() => selectMachine(ambiguous, undefined), /none marked default/);
  });

  it('honors an explicit --machine request', () => {
    assert.equal(selectMachine(twoMachines, 'podman-machine-default').Name, 'podman-machine-default');
  });

  it('rejects an explicit --machine request that does not exist', () => {
    assert.throws(() => selectMachine(twoMachines, 'nope'), /no Podman machine named "nope"/);
  });

  it('rejects an empty machine list', () => {
    assert.throws(() => selectMachine([], undefined), /podman machine init/);
  });
});

describe('formatExports', () => {
  it('formats DOCKER_HOST + the Ryuk flag for bash, PowerShell and cmd', () => {
    const { bash, powershell, cmd } = formatExports([
      ['DOCKER_HOST', 'npipe://./pipe/podman-machine-default'],
      ['TESTCONTAINERS_RYUK_DISABLED', 'true'],
    ]);

    assert.equal(
      bash,
      'export DOCKER_HOST=npipe://./pipe/podman-machine-default\nexport TESTCONTAINERS_RYUK_DISABLED=true',
    );
    assert.equal(
      powershell,
      '$env:DOCKER_HOST = "npipe://./pipe/podman-machine-default"\n$env:TESTCONTAINERS_RYUK_DISABLED = "true"',
    );
    assert.equal(cmd, 'set DOCKER_HOST=npipe://./pipe/podman-machine-default\nset TESTCONTAINERS_RYUK_DISABLED=true');
  });

  it('single-quotes a bash value that needs it, escaping embedded quotes', () => {
    const { bash } = formatExports([['X', "a b'c"]]);
    assert.equal(bash, "export X='a b'\\''c'");
  });

  it('backtick-escapes a PowerShell value that carries a double quote', () => {
    const { powershell } = formatExports([['X', 'a"b']]);
    assert.equal(powershell, '$env:X = "a`"b"');
  });
});

describe('parseHttpResponse', () => {
  function response(body, { contentLength = body.length } = {}) {
    const head = [
      'HTTP/1.1 200 OK',
      'Api-Version: 1.44',
      'Server: Libpod/5.8.6 (linux)',
      `Content-Length: ${contentLength}`,
      'Content-Type: text/plain; charset=utf-8',
      'Connection: close',
      '',
      '',
    ].join('\r\n');
    return Buffer.from(head + body, 'utf8');
  }

  it('is incomplete before the header terminator arrives', () => {
    assert.equal(parseHttpResponse(Buffer.from('HTTP/1.1 200 OK\r\nApi-Vers')).complete, false);
  });

  it('is incomplete once headers arrive but the body is still short of Content-Length', () => {
    const full = response('OK');
    const partial = full.subarray(0, full.length - 1);
    assert.equal(parseHttpResponse(partial).complete, false);
  });

  it('parses the real podman /_ping shape once the full body has arrived', () => {
    const parsed = parseHttpResponse(response('OK'));
    assert.equal(parsed.complete, true);
    assert.equal(parsed.statusCode, 200);
    assert.equal(parsed.body, 'OK');
    assert.equal(parsed.headers['api-version'], '1.44');
  });

  it('treats a response with no Content-Length as complete on arrival', () => {
    const raw = Buffer.from('HTTP/1.0 200 OK\r\nConnection: close\r\n\r\nOK', 'latin1');
    const parsed = parseHttpResponse(raw);
    assert.equal(parsed.complete, true);
    assert.equal(parsed.body, 'OK');
  });

  it('reports a non-200 status code as-is — callers decide ok from statusCode', () => {
    const notFound = Buffer.from(
      ['HTTP/1.1 404 Not Found', 'Content-Length: 0', 'Connection: close', '', ''].join('\r\n'),
      'latin1',
    );
    const parsed = parseHttpResponse(notFound);
    assert.equal(parsed.complete, true);
    assert.equal(parsed.statusCode, 404);
  });
});
