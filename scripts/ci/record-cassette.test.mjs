/**
 * Tests for scripts/ci/record-cassette.mjs — `node --test scripts/ci/record-cassette.test.mjs`.
 * Zero dependencies: node:test + node:assert/strict only — same shape as
 * scripts/commit-lint.test.mjs.
 *
 * Pure exported functions are tested directly. The CLI entrypoint
 * (`main`) is also exercised, but only with a fake `runDotnet` so no
 * `dotnet` process is spawned from these tests.
 */
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';

import {
  extractModelCassette,
  main,
  parseArgs,
  preflight,
  resolveCassettePath,
  resolveOutputPath,
  runRecord,
  verifyCassette,
} from './record-cassette.mjs';

let scratch;
before(() => {
  scratch = mkdtempSync(join(tmpdir(), 'record-cassette-test-'));
});
after(() => {
  if (scratch !== undefined) {
    rmSync(scratch, { recursive: true, force: true });
  }
});

function writeScenario(name, body) {
  const path = join(scratch, name);
  writeFileSync(path, body);
  return path;
}

// ---------------------------------------------------------------------------

describe('extractModelCassette', () => {
  it('finds the scalar value when model.cassette lives two spaces under model', () => {
    const yaml = [
      'schemaVersion: 1',
      'name: add-null-check-replay',
      'model:',
      '  mode: replay',
      '  cassette: cassettes/add-null-check.v1.json',
      'expectedTrajectory:',
      '  - stage: work-item',
    ].join('\n');

    assert.equal(extractModelCassette(yaml), 'cassettes/add-null-check.v1.json');
  });

  it('returns null when the file declares no top-level model: block', () => {
    const yaml = [
      'schemaVersion: 1',
      'name: add-null-check',
      'worker:',
      '  image: foo',
    ].join('\n');

    assert.equal(extractModelCassette(yaml), null);
  });

  it('returns null when model: exists but has no cassette: child', () => {
    const yaml = [
      'schemaVersion: 1',
      'model:',
      '  mode: fake',
      '  fakeScript: scripts/add-null-check.fake.json',
    ].join('\n');

    assert.equal(extractModelCassette(yaml), null);
  });

  it('strips a single layer of double quotes around the value', () => {
    const yaml = ['model:', '  cassette: "cassettes/quoted.v1.json"'].join('\n');
    assert.equal(extractModelCassette(yaml), 'cassettes/quoted.v1.json');
  });

  it('strips a single layer of single quotes around the value', () => {
    const yaml = ["model:", "  cassette: 'cassettes/single.v1.json'"].join('\n');
    assert.equal(extractModelCassette(yaml), 'cassettes/single.v1.json');
  });

  it('ignores model: as a substring inside a larger string on a non-header line', () => {
    const yaml = [
      'description: model: fake story',
      'model:',
      '  cassette: cassettes/found.v1.json',
    ].join('\n');

    assert.equal(extractModelCassette(yaml), 'cassettes/found.v1.json');
  });

  it('closes the model: block when a top-level key with no indent reappears', () => {
    const yaml = [
      'model:',
      '  cassette: cassettes/first.v1.json',
      'expectedTrajectory:',
      '  - stage: work-item',
      '    cassette: cassettes/should-be-ignored.v1.json',
    ].join('\n');

    assert.equal(extractModelCassette(yaml), 'cassettes/first.v1.json');
  });

  it('tolerates trailing whitespace and CRLF line endings', () => {
    const yaml = [
      'model:\r',
      '  mode:  replay   \r',
      '  cassette:   cassettes/with-space.v1.json   \r',
    ].join('');

    assert.equal(extractModelCassette(yaml), 'cassettes/with-space.v1.json');
  });

  it('treats inline comments on the model: header as still a header', () => {
    const yaml = [
      'model: # the model wiring',
      '  cassette: cassettes/inline-comment.v1.json',
    ].join('\n');

    assert.equal(extractModelCassette(yaml), 'cassettes/inline-comment.v1.json');
  });

  it('returns null for empty / non-string input', () => {
    assert.equal(extractModelCassette(''), null);
    assert.equal(extractModelCassette(undefined), null);
    assert.equal(extractModelCassette(null), null);
    assert.equal(extractModelCassette(42), null);
  });
});

describe('resolveCassettePath', () => {
  it('joins a scenario-relative path against the scenario file directory', () => {
    const scenarioPath = join(scratch, 'scenario.scenario.yaml');
    const resolved = resolveCassettePath(scenarioPath, 'cassettes/foo.v1.json');

    assert.equal(resolved, join(scratch, 'cassettes', 'foo.v1.json'));
  });

  it('returns an already-absolute path verbatim', () => {
    const scenarioPath = join(scratch, 'scenario.scenario.yaml');
    const absolute = join(scratch, 'elsewhere', 'bar.v1.json');
    assert.equal(resolveCassettePath(scenarioPath, absolute), absolute);
  });

  it('throws when the scenario path is relative', () => {
    assert.throws(() => resolveCassettePath('relative.yaml', 'foo.json'), /absolute/);
  });
});

describe('resolveOutputPath', () => {
  it('uses --cassette-out when provided (made absolute)', () => {
    const scenarioPath = join(scratch, 'scenario.scenario.yaml');
    const out = resolveOutputPath({
      scenarioPath,
      cassetteOutPath: join(scratch, 'override.v1.json'),
      scenarioText: 'model:\n  cassette: cassettes/from-scenario.v1.json',
    });

    assert.equal(out, join(scratch, 'override.v1.json'));
  });

  it('falls back to scenario.model.cassette when --cassette-out is unset', () => {
    const scenarioPath = join(scratch, 'scenario.scenario.yaml');
    const yaml = 'model:\n  cassette: cassettes/from-scenario.v1.json';
    const out = resolveOutputPath({ scenarioPath, cassetteOutPath: null, scenarioText: yaml });

    assert.equal(out, join(scratch, 'cassettes', 'from-scenario.v1.json'));
  });

  it('returns null when neither input resolves a path', () => {
    const out = resolveOutputPath({
      scenarioPath: join(scratch, 'scenario.scenario.yaml'),
      cassetteOutPath: null,
      scenarioText: 'model:\n  mode: fake',
    });
    assert.equal(out, null);
  });
});

// ---------------------------------------------------------------------------

describe('parseArgs', () => {
  it('returns the usage block and exit 1 on no args', () => {
    const result = parseArgs([]);
    assert.equal(result.kind, 'usage');
    assert.equal(result.exitCode, 1);
    assert.match(result.message, /Usage:/);
  });

  it('returns the usage block and exit 0 on --help', () => {
    const result = parseArgs(['--help']);
    assert.equal(result.kind, 'usage');
    assert.equal(result.exitCode, 0);
    assert.match(result.message, /Usage:/);
  });

  it('returns an error when --scenario is missing', () => {
    const result = parseArgs(['--upstream', 'http://127.0.0.1:17190']);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /--scenario is required/);
  });

  it('returns an error when --upstream is missing', () => {
    const result = parseArgs(['--scenario', '/tmp/foo.scenario.yaml']);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /--upstream is required/);
  });

  it('returns an error when --budget-usd is not a non-negative number', () => {
    const result = parseArgs([
      '--scenario', '/tmp/foo.scenario.yaml',
      '--upstream', 'http://127.0.0.1:17190',
      '--budget-usd', '-1',
    ]);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /--budget-usd must be a non-negative number/);
  });

  it('rejects a non-numeric --budget-usd', () => {
    const result = parseArgs([
      '--scenario', '/tmp/foo.scenario.yaml',
      '--upstream', 'http://127.0.0.1:17190',
      '--budget-usd', 'five-dollars',
    ]);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /--budget-usd must be a non-negative number/);
  });

  it('parses the happy path with all flags', () => {
    const result = parseArgs([
      '--scenario', '/tmp/foo.scenario.yaml',
      '--upstream', 'http://127.0.0.1:17190',
      '--cassette-out', '/tmp/out.json',
      '--budget-usd', '5.00',
      '--force',
    ]);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.scenarioPath, '/tmp/foo.scenario.yaml');
    assert.equal(result.options.upstreamUrl, 'http://127.0.0.1:17190');
    assert.equal(result.options.cassetteOutPath, '/tmp/out.json');
    assert.equal(result.options.budgetUsd, '5.00');
    assert.equal(result.options.force, true);
  });

  it('parses the minimal happy path (only the two required flags)', () => {
    const result = parseArgs([
      '--scenario', '/tmp/foo.scenario.yaml',
      '--upstream', 'http://127.0.0.1:17190',
    ]);
    assert.equal(result.kind, 'args');
    assert.equal(result.options.scenarioPath, '/tmp/foo.scenario.yaml');
    assert.equal(result.options.upstreamUrl, 'http://127.0.0.1:17190');
    assert.equal(result.options.cassetteOutPath, null);
    assert.equal(result.options.budgetUsd, null);
    assert.equal(result.options.force, false);
  });

  it('returns an error when --scenario has no following value', () => {
    const result = parseArgs(['--scenario']);
    assert.equal(result.kind, 'error');
    assert.match(result.message, /--scenario is required/);
  });
});

describe('preflight — --force guard', () => {
  it('blocks when the resolved cassette path already exists on disk', () => {
    const existingPath = join(scratch, 'existing.json');
    writeFileSync(existingPath, '{}');

    const verdict = preflight({ cassettePath: existingPath, force: false });
    assert.equal(verdict.ok, false);
    assert.match(verdict.reason, /already exists/);
    assert.match(verdict.reason, /--force/);
  });

  it('lets --force proceed when the cassette path already exists', () => {
    const existingPath = join(scratch, 'existing.json');
    writeFileSync(existingPath, '{}');

    const verdict = preflight({ cassettePath: existingPath, force: true });
    assert.deepEqual(verdict, { ok: true });
  });

  it('lets a fresh cassette path through', () => {
    const freshPath = join(scratch, 'fresh.json');
    const verdict = preflight({ cassettePath: freshPath, force: false });
    assert.deepEqual(verdict, { ok: true });
  });
});

// ---------------------------------------------------------------------------

describe('runRecord — child-process boundary', () => {
  it('calls the injected runDotnet with the COMUKI_RECORD_* env vars set', () => {
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const result = runRecord({
      scenarioPath: '/tmp/foo.scenario.yaml',
      upstreamUrl: 'http://127.0.0.1:17190',
      cassetteOutPath: '/tmp/out.json',
      budgetUsd: '5.00',
      projectPath: '/tmp/Comuki.EndToEnd.AgentLoop.csproj',
      env: { PATH: '/usr/bin' },
      runDotnet,
    });

    assert.equal(result.status, 0);
    assert.equal(calls.length, 1);
    assert.deepEqual(calls[0].args, [
      'run', '--project', '/tmp/Comuki.EndToEnd.AgentLoop.csproj', '--no-build',
      '--', '--filter-class', 'Comuki.EndToEnd.AgentLoop.RealPi.RecordCassetteShould',
    ]);
    assert.equal(calls[0].env.COMUKI_RECORD_SCENARIO, '/tmp/foo.scenario.yaml');
    assert.equal(calls[0].env.COMUKI_RECORD_UPSTREAM, 'http://127.0.0.1:17190');
    assert.equal(calls[0].env.COMUKI_RECORD_CASSETTE_OUT, '/tmp/out.json');
    assert.equal(calls[0].env.COMUKI_RECORD_BUDGET_USD, '5.00');
    assert.equal(calls[0].env.PATH, '/usr/bin');
  });

  it('omits the optional env vars when they are not provided', () => {
    const calls = [];
    const runDotnet = (args, env) => {
      calls.push({ args, env });
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    runRecord({
      scenarioPath: '/tmp/foo.scenario.yaml',
      upstreamUrl: 'http://127.0.0.1:17190',
      cassetteOutPath: null,
      budgetUsd: null,
      projectPath: '/tmp/proj.csproj',
      env: {},
      runDotnet,
    });

    const env = calls[0].env;
    assert.equal(env.COMUKI_RECORD_SCENARIO, '/tmp/foo.scenario.yaml');
    assert.equal(env.COMUKI_RECORD_UPSTREAM, 'http://127.0.0.1:17190');
    assert.equal(env.COMUKI_RECORD_CASSETTE_OUT, undefined);
    assert.equal(env.COMUKI_RECORD_BUDGET_USD, undefined);
  });

  it('propagates non-zero exit code without retrying', () => {
    const runDotnet = () => ({ status: 137, signal: 'SIGKILL', stdout: '', stderr: 'killed', error: null });
    const result = runRecord({
      scenarioPath: '/tmp/foo.scenario.yaml',
      upstreamUrl: 'http://127.0.0.1:17190',
      cassetteOutPath: null,
      budgetUsd: null,
      projectPath: '/tmp/proj.csproj',
      env: {},
      runDotnet,
    });

    assert.equal(result.status, 137);
  });
});

describe('verifyCassette', () => {
  it('reports the exchange count when the file exists and parses', () => {
    const path = join(scratch, 'good-cassette.json');
    writeFileSync(path, JSON.stringify({ exchanges: [{}, {}, {}] }));
    const verdict = verifyCassette(path);
    assert.equal(verdict.ok, true);
    assert.equal(verdict.exchangeCount, 3);
  });

  it('reports missing-file as not ok', () => {
    const verdict = verifyCassette(join(scratch, 'does-not-exist.json'));
    assert.equal(verdict.ok, false);
    assert.match(verdict.reason, /does not exist/);
  });

  it('reports invalid JSON as not ok', () => {
    const path = join(scratch, 'broken.json');
    writeFileSync(path, 'not json {');
    const verdict = verifyCassette(path);
    assert.equal(verdict.ok, false);
    assert.match(verdict.reason, /not valid JSON/);
  });

  it('reports an empty exchanges array as not ok', () => {
    const path = join(scratch, 'empty.json');
    writeFileSync(path, JSON.stringify({ exchanges: [] }));
    const verdict = verifyCassette(path);
    assert.equal(verdict.ok, false);
    assert.match(verdict.reason, /no top-level 'exchanges' array/);
  });

  it('reports a missing exchanges field as not ok', () => {
    const path = join(scratch, 'noex.json');
    writeFileSync(path, JSON.stringify({ scenario: 'foo' }));
    const verdict = verifyCassette(path);
    assert.equal(verdict.ok, false);
  });
});

// ---------------------------------------------------------------------------

describe('main — CLI integration (no dotnet, no real cassette)', () => {
  it('prints usage and returns exit 1 on no args', () => {
    let out = '';
    let err = '';
    const code = main([], {
      stdout: { write: (chunk) => { out += chunk; return true; } },
      stderr: { write: (chunk) => { err += chunk; return true; } },
      runDotnet: () => { throw new Error('runDotnet must not be called when args are invalid'); },
    });
    assert.equal(code, 1);
    assert.match(out, /Usage:/);
    assert.equal(err, '');
  });

  it('prints usage and returns exit 0 on --help', () => {
    let out = '';
    const code = main(['--help'], {
      stdout: { write: (chunk) => { out += chunk; return true; } },
      stderr: { write: () => true },
      runDotnet: () => { throw new Error('runDotnet must not be called on --help'); },
    });
    assert.equal(code, 0);
    assert.match(out, /Usage:/);
  });

  it('prints a clear error and returns 1 when --force is not passed and the cassette already exists', () => {
    const scenarioPath = writeScenario('scenario.scenario.yaml', 'model:\n  cassette: cassettes/already.v1.json');
    const cassettePath = join(scratch, 'cassettes', 'already.v1.json');
    mkdirSync(join(scratch, 'cassettes'), { recursive: true });
    writeFileSync(cassettePath, '{}');

    let err = '';
    const code = main(
      ['--scenario', scenarioPath, '--upstream', 'http://127.0.0.1:17190'],
      {
        stdout: { write: () => true },
        stderr: { write: (chunk) => { err += chunk; return true; } },
        runDotnet: () => { throw new Error('runDotnet must not be called when preflight fails'); },
      },
    );

    assert.equal(code, 1);
    assert.match(err, /already exists/);
    assert.match(err, /--force/);
  });

  it('proceeds past preflight with --force, surfaces dotnet stdout/stderr tail on non-zero exit', () => {
    const scenarioPath = writeScenario('force.scenario.yaml', 'model:\n  cassette: cassettes/forced.v1.json');
    mkdirSync(join(scratch, 'cassettes'), { recursive: true });
    const cassettePath = join(scratch, 'cassettes', 'forced.v1.json');
    writeFileSync(cassettePath, '{}');

    let err = '';
    let out = '';
    const runDotnet = () => ({
      status: 1,
      signal: null,
      stdout: 'a fake failure line\n',
      stderr: 'and a stderr line\n',
      error: null,
    });

    const code = main(
      ['--scenario', scenarioPath, '--upstream', 'http://127.0.0.1:17190', '--force'],
      {
        stdout: { write: (chunk) => { out += chunk; return true; } },
        stderr: { write: (chunk) => { err += chunk; return true; } },
        runDotnet,
        projectPath: '/tmp/proj.csproj',
      },
    );

    assert.equal(code, 1);
    assert.match(out, /recording scenario/);
    assert.match(err, /exited with status 1/);
    assert.match(err, /a fake failure line/);
    assert.match(err, /and a stderr line/);
  });

  it('prints a success summary when the dotnet run succeeds and the cassette is valid', () => {
    const scenarioPath = writeScenario('happy.scenario.yaml', 'model:\n  cassette: cassettes/happy.v1.json');
    mkdirSync(join(scratch, 'cassettes'), { recursive: true });
    const cassettePath = join(scratch, 'cassettes', 'happy.v1.json');
    // The cassette doesn't exist yet; the fake runDotnet "writes" it via a side effect.
    const runDotnet = () => {
      writeFileSync(cassettePath, JSON.stringify({ exchanges: [{}, {}, {}, {}] }));
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    let out = '';
    let err = '';
    const code = main(
      ['--scenario', scenarioPath, '--upstream', 'http://127.0.0.1:17190'],
      {
        stdout: { write: (chunk) => { out += chunk; return true; } },
        stderr: { write: (chunk) => { err += chunk; return true; } },
        runDotnet,
        projectPath: '/tmp/proj.csproj',
      },
    );

    assert.equal(code, 0);
    assert.match(out, /recording scenario/);
    assert.match(out, /ok — wrote 4 exchange\(s\)/);
    assert.match(out, /review 'git diff'/);
    assert.equal(err, '');
  });

  it('exits 1 when neither --cassette-out nor model.cassette resolves a path', () => {
    const scenarioPath = writeScenario('no-cassette.scenario.yaml', 'model:\n  mode: fake\n');

    let err = '';
    const code = main(
      ['--scenario', scenarioPath, '--upstream', 'http://127.0.0.1:17190'],
      {
        stdout: { write: () => true },
        stderr: { write: (chunk) => { err += chunk; return true; } },
        runDotnet: () => { throw new Error('runDotnet must not be called when path resolution fails'); },
      },
    );

    assert.equal(code, 1);
    assert.match(err, /could not resolve a cassette output path/);
  });

  it('exits 1 when the scenario file does not exist', () => {
    let err = '';
    const code = main(
      ['--scenario', join(scratch, 'definitely-not-there.scenario.yaml'), '--upstream', 'http://127.0.0.1:17190'],
      {
        stdout: { write: () => true },
        stderr: { write: (chunk) => { err += chunk; return true; } },
        runDotnet: () => { throw new Error('runDotnet must not be called when scenario cannot be read'); },
      },
    );

    assert.equal(code, 1);
    assert.match(err, /could not read scenario file/);
  });

  it('echoes the budget on the budget branch', () => {
    const scenarioPath = writeScenario('budget.scenario.yaml', 'model:\n  cassette: cassettes/budget.v1.json');
    mkdirSync(join(scratch, 'cassettes'), { recursive: true });
    const cassettePath = join(scratch, 'cassettes', 'budget.v1.json');

    let out = '';
    const runDotnet = () => {
      writeFileSync(cassettePath, JSON.stringify({ exchanges: [{}] }));
      return { status: 0, signal: null, stdout: '', stderr: '', error: null };
    };

    const code = main(
      ['--scenario', scenarioPath, '--upstream', 'http://127.0.0.1:17190', '--budget-usd', '5.00'],
      {
        stdout: { write: (chunk) => { out += chunk; return true; } },
        stderr: { write: () => true },
        runDotnet,
        projectPath: '/tmp/proj.csproj',
      },
    );

    assert.equal(code, 0);
    assert.match(out, /budget requested = \$5\.00/);
    assert.match(out, /WS9 wires enforcement/);
  });
});
