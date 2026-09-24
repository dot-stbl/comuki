/**
 * Tests for scripts/ci/dotnet-test.mjs — `node --test scripts/ci/dotnet-test.test.mjs`.
 * Zero dependencies: node:test + node:assert/strict only (same style as
 * scripts/commit-lint.test.mjs).
 *
 * These tests exercise the pure logic (arg parsing, glob matching, CTRF
 * summarizing, envelope building, markdown rendering) and project discovery
 * against a synthetic fixture tree — never the real `tests/unit`/
 * `tests/integration` trees (those are owned by other concurrent work and
 * change independently of this script) and never a real `dotnet run`.
 */
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { after, describe, it } from 'node:test';

import {
  buildEnvelope,
  discoverProjects,
  failuresFromCtrf,
  formatVerdict,
  globToRegExp,
  parseArgs,
  renderMarkdown,
  summarizeCtrf,
} from './dotnet-test.mjs';

// ---------------------------------------------------------------------------
// parseArgs
// ---------------------------------------------------------------------------

describe('parseArgs', () => {
  it('accepts --tier=unit', () => {
    const { ok, options } = parseArgs(['--tier=unit']);
    assert.ok(ok);
    assert.equal(options.tier, 'unit');
    assert.equal(options.full, false);
    assert.equal(options.build, false);
  });

  it('accepts --tier with a separate value', () => {
    const { ok, options } = parseArgs(['--tier', 'integration']);
    assert.ok(ok);
    assert.equal(options.tier, 'integration');
  });

  it('accepts --full and --build as bare flags', () => {
    const { ok, options } = parseArgs(['--tier=unit', '--full', '--build']);
    assert.ok(ok);
    assert.equal(options.full, true);
    assert.equal(options.build, true);
  });

  it('accepts --project with a glob', () => {
    const { ok, options } = parseArgs(['--tier=unit', '--project', 'Comuki.Host.*']);
    assert.ok(ok);
    assert.equal(options.project, 'Comuki.Host.*');
  });

  it('accepts --report-dir=value and --report-dir value forms', () => {
    assert.equal(parseArgs(['--tier=unit', '--report-dir=out']).options.reportDir, 'out');
    assert.equal(parseArgs(['--tier=unit', '--report-dir', 'out']).options.reportDir, 'out');
  });

  it('rejects a missing --tier', () => {
    const { ok, error } = parseArgs([]);
    assert.equal(ok, false);
    assert.match(error, /missing required --tier/);
  });

  it('rejects an unknown tier value', () => {
    const { ok, error } = parseArgs(['--tier=agent-loop']);
    assert.equal(ok, false);
    assert.match(error, /unit\|integration/);
  });

  it('rejects a value-taking flag with nothing after it', () => {
    assert.equal(parseArgs(['--tier']).ok, false);
    assert.equal(parseArgs(['--tier=unit', '--project']).ok, false);
    assert.equal(parseArgs(['--tier=unit', '--report-dir']).ok, false);
  });

  it('does not swallow the next flag as a missing value', () => {
    const { ok, error } = parseArgs(['--project', '--tier=unit']);
    assert.equal(ok, false);
    assert.match(error, /--project needs a glob/);
  });

  it('rejects an unrecognized argument', () => {
    const { ok, error } = parseArgs(['--tier=unit', '--bogus']);
    assert.equal(ok, false);
    assert.match(error, /unrecognized argument/);
  });

  it('short-circuits on --help without requiring --tier', () => {
    const { ok, options } = parseArgs(['--help']);
    assert.ok(ok);
    assert.equal(options.help, true);
  });
});

// ---------------------------------------------------------------------------
// globToRegExp
// ---------------------------------------------------------------------------

describe('globToRegExp', () => {
  it('matches a literal name exactly', () => {
    const re = globToRegExp('Comuki.Host.Unit.Auth');
    assert.ok(re.test('Comuki.Host.Unit.Auth'));
    assert.ok(!re.test('Comuki.Host.Unit.Authx'));
  });

  it('supports a trailing wildcard', () => {
    const re = globToRegExp('Comuki.Host.*');
    assert.ok(re.test('Comuki.Host.Unit.Auth'));
    assert.ok(re.test('Comuki.Host.Integration.Auth'));
    assert.ok(!re.test('Comuki.Modules.Chat.Unit'));
  });

  it('escapes regex-special characters', () => {
    const re = globToRegExp('Comuki.Host.Unit.Auth');
    assert.ok(!re.test('ComukiXHostXUnitXAuth'));
  });
});

// ---------------------------------------------------------------------------
// summarizeCtrf / failuresFromCtrf
// ---------------------------------------------------------------------------

function ctrfFixture({ tests = 0, passed = 0, failed = 0, pending = 0, skipped = 0, other = 0, testList = [] } = {}) {
  return {
    results: {
      summary: { tests, passed, failed, pending, skipped, other },
      tests: testList,
    },
  };
}

describe('summarizeCtrf', () => {
  it('reads the summary counts', () => {
    const ctrf = ctrfFixture({ tests: 24, passed: 24 });
    assert.deepEqual(summarizeCtrf(ctrf), { total: 24, passed: 24, failed: 0, skipped: 0 });
  });

  it('folds pending+skipped+other into skipped', () => {
    const ctrf = ctrfFixture({ tests: 10, passed: 7, pending: 1, skipped: 1, other: 1 });
    assert.deepEqual(summarizeCtrf(ctrf), { total: 10, passed: 7, failed: 0, skipped: 3 });
  });

  it('defaults every field to 0 on a malformed/empty object', () => {
    assert.deepEqual(summarizeCtrf({}), { total: 0, passed: 0, failed: 0, skipped: 0 });
  });
});

describe('failuresFromCtrf', () => {
  it('emits one entry per failed test, taking only the first message line', () => {
    const ctrf = ctrfFixture({
      tests: 2,
      passed: 1,
      failed: 1,
      testList: [
        { name: 'Should pass', status: 'passed' },
        {
          name: 'Should throw on null',
          status: 'failed',
          message: 'Shouldly.ShouldAssertException : x\n    should be y\nbut was z',
        },
      ],
    });
    const failures = failuresFromCtrf('Comuki.Example.Unit', ctrf, ['run.log']);
    assert.deepEqual(failures, [
      {
        scenario: 'Comuki.Example.Unit',
        stage: 'Should throw on null',
        message: 'Shouldly.ShouldAssertException : x',
        artifactPaths: ['run.log'],
      },
    ]);
  });

  it('falls back to a generic message when CTRF omits one', () => {
    const ctrf = ctrfFixture({
      tests: 1,
      failed: 1,
      testList: [{ name: 'Should X', status: 'failed' }],
    });
    const [failure] = failuresFromCtrf('P', ctrf, []);
    assert.equal(failure.message, 'test failed');
  });

  it('ignores passed/skipped tests', () => {
    const ctrf = ctrfFixture({
      tests: 2,
      passed: 1,
      skipped: 1,
      testList: [
        { name: 'a', status: 'passed' },
        { name: 'b', status: 'skipped' },
      ],
    });
    assert.deepEqual(failuresFromCtrf('P', ctrf, []), []);
  });
});

// ---------------------------------------------------------------------------
// buildEnvelope / formatVerdict / renderMarkdown
// ---------------------------------------------------------------------------

describe('buildEnvelope', () => {
  it('matches design.md\'s envelope shape', () => {
    const report = buildEnvelope({
      tier: 'unit',
      startedAt: '2026-09-23T12:00:00.000Z',
      durationMs: 1234,
      summary: { total: 10, passed: 9, failed: 1, skipped: 0 },
      failures: [{ scenario: 'P', stage: 'T', message: 'm', artifactPaths: [] }],
    });
    assert.deepEqual(Object.keys(report).sort(), [
      'cost',
      'durationMs',
      'failures',
      'mode',
      'schemaVersion',
      'startedAt',
      'summary',
      'tier',
    ]);
    assert.equal(report.schemaVersion, 1);
    assert.equal(report.mode, null);
    assert.deepEqual(report.cost, { usdMicros: 0, tokensIn: 0, tokensOut: 0 });
  });
});

describe('formatVerdict', () => {
  it('reports PASS when nothing failed', () => {
    const report = buildEnvelope({
      tier: 'unit',
      startedAt: 'x',
      durationMs: 0,
      summary: { total: 5, passed: 5, failed: 0, skipped: 0 },
      failures: [],
    });
    assert.equal(formatVerdict(report), 'PASS 5/5');
  });

  it('reports FAIL with a pointer to report.md when something failed', () => {
    const report = buildEnvelope({
      tier: 'unit',
      startedAt: 'x',
      durationMs: 0,
      summary: { total: 5, passed: 3, failed: 2, skipped: 0 },
      failures: [],
    });
    assert.equal(formatVerdict(report), 'FAIL 2/5 — see report.md');
  });
});

describe('renderMarkdown', () => {
  it('renders a clean pass report with no failures table', () => {
    const report = buildEnvelope({
      tier: 'unit',
      startedAt: '2026-09-23T12:00:00.000Z',
      durationMs: 500,
      summary: { total: 3, passed: 3, failed: 0, skipped: 0 },
      failures: [],
    });
    const md = renderMarkdown(report);
    assert.match(md, /# Test report — unit/);
    assert.match(md, /\*\*PASS 3\/3\*\*/);
    assert.match(md, /No failures\./);
    assert.doesNotMatch(md, /## Failures/);
  });

  it('renders a failures table and a first-failure section from the same object', () => {
    const report = buildEnvelope({
      tier: 'integration',
      startedAt: '2026-09-23T12:00:00.000Z',
      durationMs: 500,
      summary: { total: 3, passed: 1, failed: 2, skipped: 0 },
      failures: [
        {
          scenario: 'Comuki.Host.Integration.Auth',
          stage: 'ShouldRejectExpiredToken',
          message: 'expected 401, got 200',
          artifactPaths: ['artifacts/test-reports/integration/projects/Comuki.Host.Integration.Auth/run.log'],
        },
        {
          scenario: 'Comuki.Host.Integration.Runs',
          stage: 'ShouldCreateRun',
          message: 'timeout',
          artifactPaths: [],
        },
      ],
    });
    const md = renderMarkdown(report);
    assert.match(md, /## Failures/);
    assert.match(md, /\| Comuki\.Host\.Integration\.Auth \| ShouldRejectExpiredToken \| expected 401, got 200 \|/);
    assert.match(md, /## First failure/);
    assert.match(md, /\*\*Comuki\.Host\.Integration\.Auth\*\* — ShouldRejectExpiredToken/);
    assert.match(md, /artifacts\/test-reports\/integration\/projects\/Comuki\.Host\.Integration\.Auth\/run\.log/);
  });

  it('escapes pipe characters in a failure message so the table does not break', () => {
    const report = buildEnvelope({
      tier: 'unit',
      startedAt: 'x',
      durationMs: 0,
      summary: { total: 1, passed: 0, failed: 1, skipped: 0 },
      failures: [{ scenario: 'P', stage: 'T', message: 'a | b', artifactPaths: [] }],
    });
    assert.match(renderMarkdown(report), /a \\\| b/);
  });
});

// ---------------------------------------------------------------------------
// discoverProjects — against a synthetic fixture tree, never the real repo
// ---------------------------------------------------------------------------

describe('discoverProjects', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'dotnet-test-fixture-'));

  function makeProject(base, name) {
    const dir = path.join(root, base, name);
    mkdirSync(dir, { recursive: true });
    writeFileSync(path.join(dir, `${name}.csproj`), '<Project />');
  }

  makeProject('tests/unit', 'Comuki.Host.Unit.Auth');
  makeProject('tests/unit', 'Comuki.Host.Unit.Cors');
  makeProject('tests/unit', 'Comuki.Modules.Chat.Unit');
  makeProject('tests', 'Comuki.Architecture.Tests');
  makeProject('tests/integration', 'Comuki.Host.Integration.Auth');
  makeProject('tests/integration', 'Comuki.Host.Integration.Runs');
  makeProject('tests/integration', 'Comuki.Host.Testing');

  after(() => rmSync(root, { recursive: true, force: true }));

  it('discovers tests/unit/*/ for tier=unit without --full', () => {
    const projects = discoverProjects({ tier: 'unit', repoRoot: root });
    assert.deepEqual(
      projects.map((p) => p.name),
      ['Comuki.Host.Unit.Auth', 'Comuki.Host.Unit.Cors', 'Comuki.Modules.Chat.Unit'],
    );
  });

  it('adds Comuki.Architecture.Tests only with --full', () => {
    const projects = discoverProjects({ tier: 'unit', full: true, repoRoot: root });
    assert.deepEqual(
      projects.map((p) => p.name),
      ['Comuki.Architecture.Tests', 'Comuki.Host.Unit.Auth', 'Comuki.Host.Unit.Cors', 'Comuki.Modules.Chat.Unit'],
    );
  });

  it('discovers tests/integration/*/ minus Comuki.Host.Testing for tier=integration', () => {
    const projects = discoverProjects({ tier: 'integration', repoRoot: root });
    assert.deepEqual(
      projects.map((p) => p.name),
      ['Comuki.Host.Integration.Auth', 'Comuki.Host.Integration.Runs'],
    );
  });

  it('narrows by --project glob', () => {
    const projects = discoverProjects({ tier: 'unit', projectGlob: 'Comuki.Host.*', repoRoot: root });
    assert.deepEqual(
      projects.map((p) => p.name),
      ['Comuki.Host.Unit.Auth', 'Comuki.Host.Unit.Cors'],
    );
  });

  it('combines --full and --project', () => {
    const projects = discoverProjects({
      tier: 'unit',
      full: true,
      projectGlob: 'Comuki.Arch*',
      repoRoot: root,
    });
    assert.deepEqual(
      projects.map((p) => p.name),
      ['Comuki.Architecture.Tests'],
    );
  });

  it('returns each project\'s expected csproj path', () => {
    const [project] = discoverProjects({ tier: 'unit', projectGlob: 'Comuki.Host.Unit.Auth', repoRoot: root });
    assert.equal(
      project.csproj,
      path.join(root, 'tests', 'unit', 'Comuki.Host.Unit.Auth', 'Comuki.Host.Unit.Auth.csproj'),
    );
  });

  it('returns an empty array when the tier directory does not exist', () => {
    const emptyRoot = mkdtempSync(path.join(tmpdir(), 'dotnet-test-empty-'));
    try {
      assert.deepEqual(discoverProjects({ tier: 'unit', repoRoot: emptyRoot }), []);
    } finally {
      rmSync(emptyRoot, { recursive: true, force: true });
    }
  });
});
