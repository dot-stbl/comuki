#!/usr/bin/env node
/**
 * podman-up — detects/starts a local Podman machine (Windows/WSL) or
 * verifies a reachable Podman/Docker socket (Linux/macOS), then prints the
 * exact env-var exports Testcontainers .NET needs, for bash, PowerShell and
 * cmd.exe.
 *
 * Never exports into the caller's shell: a child process (node/bun) cannot
 * push environment variables back into its parent — Windows/PowerShell/cmd
 * have no mechanism for that any more than POSIX does. Print + copy/paste
 * the block for your shell is the only thing that actually works.
 *
 * Usage:
 *   bun scripts/test-env/podman-up.mjs              detect/start, print exports
 *   bun scripts/test-env/podman-up.mjs --check       also ping the Docker API
 *   bun scripts/test-env/podman-up.mjs --machine=foo non-default machine name
 *
 * Validated 2026-09-23 against Podman 5.8.2 (podman-machine-default, WSL
 * backend, rootless) and Testcontainers.PostgreSql via
 * tests/integration/Comuki.Modules.Identity.Integration.Migrations — see
 * .agents/rules/process/local-test-runtime.md for what was actually needed
 * and why (in particular: DOCKER_HOST uses TWO slashes after `npipe:`, not
 * the four-slash Docker-Desktop convention design.md assumed).
 */
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import net from 'node:net';
import process from 'node:process';
import { pathToFileURL } from 'node:url';

/**
 * A container-runtime detection/configuration failure — a bad `podman`
 * invocation, an unreadable machine, or a DOCKER_HOST value this script
 * can't make sense of. Always carries a message that already names the
 * fix (see .agents/rules/process/local-test-runtime.md's troubleshooting
 * table for the same list in prose).
 */
export class PodmanRuntimeError extends Error {
  constructor(message) {
    super(message);
    this.name = 'PodmanRuntimeError';
  }
}

// ---------------------------------------------------------------------------
// Pure contract — Windows named-pipe <-> DOCKER_HOST conversion
// ---------------------------------------------------------------------------

const WINDOWS_PIPE_PREFIX = '\\\\.\\pipe\\';

/**
 * Convert a Win32 named-pipe path (as `podman machine inspect` reports it,
 * e.g. `\\.\pipe\podman-machine-default`) into the DOCKER_HOST value
 * Testcontainers .NET (Docker.DotNet) actually accepts.
 *
 * Two slashes, not four: `npipe://./pipe/<name>`. The commonly-cited
 * Docker Desktop / docker-compose convention (`npipe:////./pipe/<name>`,
 * four slashes) throws `InvalidOperationException: The endpoint is not a
 * npipe URI.` out of `TestcontainersSettings.get_OS()` — confirmed against
 * this repo's Testcontainers.PostgreSql package on 2026-09-23.
 */
export function pipePathToDockerHost(pipePath) {
  if (!pipePath.startsWith(WINDOWS_PIPE_PREFIX)) {
    throw new PodmanRuntimeError(`unrecognized Windows named pipe path: ${pipePath}`);
  }
  const name = pipePath.slice(WINDOWS_PIPE_PREFIX.length);
  return `npipe://./pipe/${name}`;
}

/** POSIX (Linux/macOS) socket path -> DOCKER_HOST value. */
export function posixSocketToDockerHost(socketPath) {
  return `unix://${socketPath}`;
}

/**
 * A DOCKER_HOST value (npipe://, unix:// or tcp://) -> something
 * {@link pingDockerApi} can connect to. Accepts both the two-slash and the
 * four-slash npipe forms so a value copy/pasted from elsewhere still works.
 */
export function dockerHostToConnectTarget(dockerHost) {
  if (dockerHost.startsWith('npipe://')) {
    const rest = dockerHost.slice('npipe://'.length);
    return { kind: 'npipe', path: `${WINDOWS_PIPE_PREFIX}${extractPipeName(rest)}` };
  }
  if (dockerHost.startsWith('unix://')) {
    return { kind: 'unix', path: dockerHost.slice('unix://'.length) };
  }
  if (dockerHost.startsWith('tcp://')) {
    const url = new URL(dockerHost.replace('tcp://', 'http://'));
    return { kind: 'tcp', host: url.hostname, port: Number(url.port || 2375) };
  }
  throw new PodmanRuntimeError(`unsupported DOCKER_HOST scheme: ${dockerHost}`);
}

/** Strip leading `/` and `./` segments off an npipe URI's authority+path, down to the pipe name. */
function extractPipeName(rest) {
  const cleaned = rest.replace(/^\/+/, '').replace(/^\.\//, '');
  const marker = 'pipe/';
  const index = cleaned.indexOf(marker);
  if (index === -1) {
    throw new PodmanRuntimeError(`cannot find a pipe name in DOCKER_HOST value: npipe://${rest}`);
  }
  return cleaned.slice(index + marker.length);
}

// ---------------------------------------------------------------------------
// Pure contract — machine selection
// ---------------------------------------------------------------------------

/**
 * Pick the Podman machine to use out of `podman machine list --format json`.
 * @param {ReadonlyArray<{Name: string, Default?: boolean}>} machines
 * @param {string|undefined} requestedName
 */
export function selectMachine(machines, requestedName) {
  if (machines.length === 0) {
    throw new PodmanRuntimeError('no Podman machine found — run `podman machine init` first');
  }
  if (requestedName) {
    const found = machines.find((machine) => machine.Name === requestedName);
    if (!found) {
      throw new PodmanRuntimeError(
        `no Podman machine named "${requestedName}" (have: ${machines.map((machine) => machine.Name).join(', ')})`,
      );
    }
    return found;
  }
  const marked = machines.find((machine) => machine.Default === true);
  if (marked) {
    return marked;
  }
  if (machines.length === 1) {
    return machines[0];
  }
  const conventional = machines.find((machine) => machine.Name === 'podman-machine-default');
  if (conventional) {
    return conventional;
  }
  throw new PodmanRuntimeError(
    `multiple Podman machines and none marked default: ${machines.map((machine) => machine.Name).join(', ')} — pass --machine=<name>`,
  );
}

// ---------------------------------------------------------------------------
// Pure contract — env export formatting
// ---------------------------------------------------------------------------

/**
 * Format one set of NAME=value pairs for bash/zsh, PowerShell and cmd.exe.
 * @param {ReadonlyArray<readonly [string, string]>} vars
 */
export function formatExports(vars) {
  return {
    bash: vars.map(([name, value]) => `export ${name}=${bashQuote(value)}`).join('\n'),
    powershell: vars.map(([name, value]) => `$env:${name} = ${powershellQuote(value)}`).join('\n'),
    cmd: vars.map(([name, value]) => `set ${name}=${value}`).join('\n'),
  };
}

const BASH_SAFE_VALUE = /^[A-Za-z0-9_:./-]+$/;

function bashQuote(value) {
  return BASH_SAFE_VALUE.test(value) ? value : `'${value.replaceAll("'", "'\\''")}'`;
}

function powershellQuote(value) {
  return `"${value.replaceAll('"', '`"')}"`;
}

// ---------------------------------------------------------------------------
// Pure contract — raw HTTP/1.1 response parsing (for --check)
// ---------------------------------------------------------------------------

/**
 * Parse as much of a raw HTTP/1.1 response as `buffer` holds. Returns
 * `{complete: false}` until the status line + headers + a full body (per
 * Content-Length, when present) have arrived — callers keep appending
 * chunks and re-parsing until `complete` is true.
 */
export function parseHttpResponse(buffer) {
  const text = buffer.toString('latin1');
  const headerEnd = text.indexOf('\r\n\r\n');
  if (headerEnd === -1) {
    return { complete: false };
  }

  const [statusLine, ...headerLines] = text.slice(0, headerEnd).split('\r\n');
  const statusMatch = /^HTTP\/\d\.\d (\d{3})/.exec(statusLine);
  const statusCode = statusMatch ? Number(statusMatch[1]) : null;

  const headers = {};
  for (const line of headerLines) {
    const separator = line.indexOf(':');
    if (separator === -1) {
      continue;
    }
    headers[line.slice(0, separator).trim().toLowerCase()] = line.slice(separator + 1).trim();
  }

  const bodyStart = headerEnd + 4;
  const contentLength = headers['content-length'] === undefined ? null : Number(headers['content-length']);
  const bodyBytesAvailable = buffer.length - bodyStart;
  const complete = contentLength === null || bodyBytesAvailable >= contentLength;
  const bodyEnd = contentLength === null ? buffer.length : bodyStart + contentLength;

  return { complete, statusCode, headers, body: buffer.slice(bodyStart, bodyEnd).toString('utf8') };
}

// ---------------------------------------------------------------------------
// I/O — podman CLI
// ---------------------------------------------------------------------------

function commandExists(command) {
  const probe = spawnSync(command, ['--version']);
  return probe.error === undefined || probe.error.code !== 'ENOENT';
}

function listPodmanMachines() {
  const result = spawnSync('podman', ['machine', 'list', '--format', 'json'], { encoding: 'utf8' });
  if (result.status !== 0) {
    throw new PodmanRuntimeError(`podman machine list failed: ${result.stderr || result.stdout}`);
  }
  return JSON.parse(result.stdout);
}

function startPodmanMachine(name) {
  const result = spawnSync('podman', ['machine', 'start', name], { stdio: 'inherit' });
  if (result.status !== 0) {
    throw new PodmanRuntimeError(`podman machine start ${name} failed (exit ${result.status})`);
  }
}

function inspectPodmanMachine(name) {
  const result = spawnSync('podman', ['machine', 'inspect', name], { encoding: 'utf8' });
  if (result.status !== 0) {
    throw new PodmanRuntimeError(`podman machine inspect ${name} failed: ${result.stderr || result.stdout}`);
  }
  const parsed = JSON.parse(result.stdout);
  return Array.isArray(parsed) ? parsed[0] : parsed;
}

// ---------------------------------------------------------------------------
// I/O — Docker-API ping (--check)
// ---------------------------------------------------------------------------

/** GET /_ping over a raw socket (Windows named pipe, POSIX unix socket, or TCP). */
export function pingDockerApi(connectTarget, { timeoutMs = 5000 } = {}) {
  return new Promise((resolve) => {
    const connectOptions =
      connectTarget.kind === 'tcp'
        ? { host: connectTarget.host, port: connectTarget.port }
        : { path: connectTarget.path };

    const socket = net.connect(connectOptions);
    let buffer = Buffer.alloc(0);
    let settled = false;

    const timer = setTimeout(() => {
      finish({ ok: false, error: 'timed out waiting for a Docker API response' });
    }, timeoutMs);

    function finish(result) {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      socket.destroy();
      resolve(result);
    }

    socket.on('connect', () => {
      socket.write('GET /_ping HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n');
    });
    socket.on('data', (chunk) => {
      buffer = Buffer.concat([buffer, chunk]);
      const parsed = parseHttpResponse(buffer);
      if (parsed.complete) {
        finish({ ok: parsed.statusCode === 200, statusCode: parsed.statusCode, body: parsed.body });
      }
    });
    socket.on('error', (error) => finish({ ok: false, error: error.message }));
  });
}

// ---------------------------------------------------------------------------
// Recipe — the env vars this repo's Testcontainers-based suites need
// ---------------------------------------------------------------------------

/** @returns {ReadonlyArray<readonly [string, string]>} */
function recipeVars(dockerHost) {
  const vars = [];
  if (dockerHost !== null) {
    vars.push(['DOCKER_HOST', dockerHost]);
  }
  // Ryuk (the Testcontainers reaper) worked fine against this exact rootless
  // podman-machine-default setup when left enabled (validated 2026-09-23),
  // but it costs an extra container per run and is one more moving part in
  // rootless Podman's networking. Disabling it is the documented default —
  // see .agents/rules/process/local-test-runtime.md for the trade-off and
  // when you would want to re-enable it.
  vars.push(['TESTCONTAINERS_RYUK_DISABLED', 'true']);
  return vars;
}

function printRecipe({ dockerHost, rootful }) {
  const { bash, powershell, cmd } = formatExports(recipeVars(dockerHost));

  console.log('');
  console.log('Export these before running Testcontainers-based test projects:');
  console.log('');
  console.log('# bash / zsh / git-bash');
  console.log(bash);
  console.log('');
  console.log('# PowerShell');
  console.log(powershell);
  console.log('');
  console.log('# cmd.exe');
  console.log(cmd);
  console.log('');
  if (rootful === false) {
    console.log(
      'Note: rootless Podman machine. See .agents/rules/process/local-test-runtime.md for why\n' +
        'TESTCONTAINERS_RYUK_DISABLED is the default here and what TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE\n' +
        'is for (only relevant when the test process itself runs inside a container).',
    );
    console.log('');
  }
  console.log('Verify connectivity: bun scripts/test-env/podman-up.mjs --check');
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

const USAGE = `podman-up — detect/start a local container runtime for Testcontainers

Usage:
  bun scripts/test-env/podman-up.mjs               detect/start Podman, print env exports
  bun scripts/test-env/podman-up.mjs --check        ping the Docker API and report status
  bun scripts/test-env/podman-up.mjs --machine=NAME use a non-default Podman machine (Windows)

Rule doc: .agents/rules/process/local-test-runtime.md
`;

function parseArgs(argv) {
  const args = { help: false, check: false, machine: undefined };
  for (const token of argv) {
    if (token === '--help' || token === '-h') {
      args.help = true;
    } else if (token === '--check') {
      args.check = true;
    } else if (token.startsWith('--machine=')) {
      args.machine = token.slice('--machine='.length);
    } else {
      console.error(`Unknown argument: ${token}`);
      args.help = true;
    }
  }
  return args;
}

function detectWindows(machineName) {
  if (!commandExists('podman')) {
    throw new PodmanRuntimeError('podman not found on PATH — install Podman Desktop / the podman CLI first');
  }
  const machine = selectMachine(listPodmanMachines(), machineName);
  if (!machine.Running) {
    console.log(`Podman machine "${machine.Name}" is stopped — starting it (can take up to ~30s)…`);
    startPodmanMachine(machine.Name);
  }
  const inspected = inspectPodmanMachine(machine.Name);
  const pipePath = inspected?.ConnectionInfo?.PodmanPipe?.Path;
  if (!pipePath) {
    throw new PodmanRuntimeError(
      `podman machine inspect ${machine.Name} did not report ConnectionInfo.PodmanPipe.Path — unexpected podman version/output shape`,
    );
  }
  return { dockerHost: pipePathToDockerHost(pipePath), rootful: inspected.Rootful };
}

function detectPosix() {
  if (commandExists('podman')) {
    const machineListing = spawnSync('podman', ['machine', 'list', '--format', 'json'], { encoding: 'utf8' });
    const machines = machineListing.status === 0 ? JSON.parse(machineListing.stdout) : [];
    if (machines.length > 0) {
      // macOS: podman also runs a VM, same shape as Windows minus the named pipe.
      const machine = selectMachine(machines, undefined);
      if (!machine.Running) {
        console.log(`Podman machine "${machine.Name}" is stopped — starting it (can take up to ~30s)…`);
        startPodmanMachine(machine.Name);
      }
      const inspected = inspectPodmanMachine(machine.Name);
      const socketPath = inspected?.ConnectionInfo?.PodmanSocket?.Path;
      if (!socketPath) {
        throw new PodmanRuntimeError(`podman machine inspect ${machine.Name} did not report ConnectionInfo.PodmanSocket.Path`);
      }
      return { dockerHost: posixSocketToDockerHost(socketPath), rootful: inspected.Rootful };
    }

    // Native Linux: rootless podman.sock under XDG_RUNTIME_DIR, started via
    // `systemctl --user enable --now podman.socket` or `podman system service`.
    const uid = typeof process.getuid === 'function' ? process.getuid() : null;
    const runtimeDir = process.env.XDG_RUNTIME_DIR ?? (uid === null ? null : `/run/user/${uid}`);
    const socketPath = runtimeDir === null ? null : `${runtimeDir}/podman/podman.sock`;
    if (socketPath !== null && existsSync(socketPath)) {
      return { dockerHost: posixSocketToDockerHost(socketPath), rootful: false };
    }
    throw new PodmanRuntimeError(
      `no active rootless Podman socket found${socketPath ? ` at ${socketPath}` : ''}. Start it once with:\n` +
        '  systemctl --user enable --now podman.socket\n' +
        'or run it ad hoc with:\n' +
        `  podman system service --time=0 unix://${socketPath ?? '$XDG_RUNTIME_DIR/podman/podman.sock'} &`,
    );
  }

  if (commandExists('docker')) {
    console.log('Docker CLI detected — Testcontainers should work with no extra DOCKER_HOST.');
    return { dockerHost: null, rootful: null };
  }

  throw new PodmanRuntimeError('neither podman nor docker found on PATH');
}

async function runDetect(args) {
  const recipe = process.platform === 'win32' ? detectWindows(args.machine) : detectPosix();
  printRecipe(recipe);
  return 0;
}

async function runCheck(args) {
  let target;
  const existing = process.env.DOCKER_HOST;
  if (existing) {
    console.log(`Checking DOCKER_HOST from the environment: ${existing}`);
    target = dockerHostToConnectTarget(existing);
  } else {
    console.log('DOCKER_HOST is not set — auto-detecting…');
    const recipe = process.platform === 'win32' ? detectWindows(args.machine) : detectPosix();
    if (recipe.dockerHost === null) {
      console.log('Nothing to ping explicitly (plain Docker, default endpoint).');
      return 0;
    }
    console.log(`Checking: ${recipe.dockerHost}`);
    target = dockerHostToConnectTarget(recipe.dockerHost);
  }

  const result = await pingDockerApi(target);
  if (result.ok) {
    console.log(`OK — Docker-compatible API responded: ${(result.body ?? '').trim() || `HTTP ${result.statusCode}`}`);
    return 0;
  }
  console.error(`FAILED — ${result.error ?? `HTTP ${result.statusCode}`}`);
  return 1;
}

/** @param {readonly string[]} argv */
export async function main(argv) {
  const args = parseArgs(argv);
  if (args.help) {
    process.stdout.write(USAGE);
    return 0;
  }
  try {
    return await (args.check ? runCheck(args) : runDetect(args));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  process.exitCode = await main(process.argv.slice(2));
}
