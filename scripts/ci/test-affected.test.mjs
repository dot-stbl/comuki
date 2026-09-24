/**
 * Tests for scripts/ci/test-affected.mjs — `node --test scripts/ci/test-affected.test.mjs`.
 * Zero dependencies: node:test + node:assert/strict only (same style as
 * scripts/commit-lint.test.mjs / scripts/ci/dotnet-test.test.mjs).
 *
 * Exercises the pure mapping logic (classification, csproj/ProjectReference
 * parsing, reverse-graph closure, plan building, openspec JSON summarizing)
 * against synthetic fixtures — never the real `platform/`/`tests/` trees
 * (those change independently of this script) and never a real `dotnet run`
 * / `bun` / `openspec` invocation.
 */
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { after, describe, it } from 'node:test';

import {
  buildPlan,
  buildProjectGraph,
  classifyChangedFiles,
  classifyProjectPaths,
  closureOverReverseGraph,
  dashboardVitestMode,
  discoverScriptTestFiles,
  findOwningProject,
  loadProjectGraph,
  openspecItemsFromChangedFiles,
  parseArgs,
  parseNodeTestOutput,
  parseProjectReferences,
  renderPlanText,
  resolveBaseRef,
  resolveProjectReference,
  reverseGraph,
  summarizeOpenspecValidation,
} from './test-affected.mjs';

// ---------------------------------------------------------------------------
// parseArgs
// ---------------------------------------------------------------------------

describe('parseArgs', () => {
  it('defaults everything to unset/false', () => {
    const { ok, options } = parseArgs([]);
    assert.ok(ok);
    assert.equal(options.base, undefined);
    assert.equal(options.dryRun, false);
    assert.equal(options.includeIntegration, false);
    assert.equal(options.reportDir, undefined);
  });

  it('accepts --dry-run and --include-integration as bare flags', () => {
    const { ok, options } = parseArgs(['--dry-run', '--include-integration']);
    assert.ok(ok);
    assert.equal(options.dryRun, true);
    assert.equal(options.includeIntegration, true);
  });

  it('accepts --base=value and --base value forms', () => {
    assert.equal(parseArgs(['--base=origin/master']).options.base, 'origin/master');
    assert.equal(parseArgs(['--base', 'origin/master']).options.base, 'origin/master');
  });

  it('accepts --report-dir=value and --report-dir value forms', () => {
    assert.equal(parseArgs(['--report-dir=out']).options.reportDir, 'out');
    assert.equal(parseArgs(['--report-dir', 'out']).options.reportDir, 'out');
  });

  it('rejects a value-taking flag with nothing after it', () => {
    assert.equal(parseArgs(['--base']).ok, false);
    assert.equal(parseArgs(['--report-dir']).ok, false);
  });

  it('does not swallow the next flag as a missing value', () => {
    const { ok, error } = parseArgs(['--base', '--dry-run']);
    assert.equal(ok, false);
    assert.match(error, /--base needs a ref/);
  });

  it('rejects an unrecognized argument', () => {
    const { ok, error } = parseArgs(['--bogus']);
    assert.equal(ok, false);
    assert.match(error, /unrecognized argument/);
  });

  it('short-circuits on --help', () => {
    const { ok, options } = parseArgs(['--help']);
    assert.ok(ok);
    assert.equal(options.help, true);
  });
});

// ---------------------------------------------------------------------------
// resolveBaseRef
// ---------------------------------------------------------------------------

describe('resolveBaseRef', () => {
  it('uses an explicit base regardless of what exists', () => {
    assert.equal(resolveBaseRef('feature/foo', () => false), 'feature/foo');
  });

  it('prefers origin/master when it resolves', () => {
    assert.equal(
      resolveBaseRef(undefined, (ref) => ref === 'origin/master'),
      'origin/master',
    );
  });

  it('falls back to master when origin/master does not resolve', () => {
    assert.equal(
      resolveBaseRef(undefined, () => false),
      'master',
    );
  });
});

// ---------------------------------------------------------------------------
// classifyChangedFiles
// ---------------------------------------------------------------------------

describe('classifyChangedFiles', () => {
  it('buckets each stack by its top-level prefix', () => {
    const c = classifyChangedFiles([
      'platform/src/modules/Costs/Comuki.Modules.Costs.Domain/Money.cs',
      'dashboard/src/app/App.tsx',
      'cli/src/index.ts',
      'agents/comuki-worker-sdk/src/index.ts',
      'scripts/ci/dotnet-test.mjs',
      'openspec/changes/add-thing/proposal.md',
      'README.md',
    ]);
    assert.deepEqual(c.dotnet, ['platform/src/modules/Costs/Comuki.Modules.Costs.Domain/Money.cs']);
    assert.deepEqual(c.dashboard, ['dashboard/src/app/App.tsx']);
    assert.deepEqual(c.cli, ['cli/src/index.ts']);
    assert.deepEqual(c.agentsPkg, ['agents/comuki-worker-sdk/src/index.ts']);
    assert.deepEqual(c.scripts, ['scripts/ci/dotnet-test.mjs']);
    assert.deepEqual(c.openspec, ['openspec/changes/add-thing/proposal.md']);
    assert.deepEqual(c.other, ['README.md']);
  });

  it('does not mistake .agents/ (rules/planning) for agents/ (TS SDK)', () => {
    const c = classifyChangedFiles(['.agents/rules/process/allowed-scripts.md']);
    assert.deepEqual(c.agentsPkg, []);
    assert.deepEqual(c.other, ['.agents/rules/process/allowed-scripts.md']);
  });

  it('catches .csproj as well as .cs', () => {
    const c = classifyChangedFiles(['platform/src/host/Comuki.Host/Comuki.Host.csproj']);
    assert.deepEqual(c.dotnet, ['platform/src/host/Comuki.Host/Comuki.Host.csproj']);
  });

  it('an all-docs diff has nothing in any test-relevant bucket', () => {
    const c = classifyChangedFiles(['DESIGN.md', 'PRODUCT.md', '.agents/STATE.md']);
    assert.equal(c.dotnet.length, 0);
    assert.equal(c.dashboard.length, 0);
    assert.equal(c.cli.length, 0);
    assert.equal(c.agentsPkg.length, 0);
    assert.equal(c.scripts.length, 0);
    assert.equal(c.openspec.length, 0);
    assert.equal(c.other.length, 3);
  });
});

// ---------------------------------------------------------------------------
// parseProjectReferences / resolveProjectReference
// ---------------------------------------------------------------------------

describe('parseProjectReferences', () => {
  it('extracts a self-closing ProjectReference', () => {
    const xml = `<Project><ItemGroup><ProjectReference Include="..\\..\\Foo\\Foo.csproj" /></ItemGroup></Project>`;
    assert.deepEqual(parseProjectReferences(xml), ['..\\..\\Foo\\Foo.csproj']);
  });

  it('extracts an open/close ProjectReference', () => {
    const xml = `<ProjectReference Include="../Bar/Bar.csproj"></ProjectReference>`;
    assert.deepEqual(parseProjectReferences(xml), ['../Bar/Bar.csproj']);
  });

  it('extracts multiple references in document order', () => {
    const xml = `
      <ProjectReference Include="../A/A.csproj" />
      <ProjectReference Include="../B/B.csproj" />
    `;
    assert.deepEqual(parseProjectReferences(xml), ['../A/A.csproj', '../B/B.csproj']);
  });

  it('returns an empty array when there are none', () => {
    assert.deepEqual(parseProjectReferences('<Project><PropertyGroup /></Project>'), []);
  });
});

describe('resolveProjectReference', () => {
  it('resolves a backslash-separated relative reference to a posix repo-relative path', () => {
    const resolved = resolveProjectReference(
      'tests/unit/Comuki.Modules.Costs.Unit/Comuki.Modules.Costs.Unit.csproj',
      '..\\..\\..\\platform\\src\\modules\\Costs\\Comuki.Modules.Costs.Application\\Comuki.Modules.Costs.Application.csproj',
    );
    assert.equal(
      resolved,
      'platform/src/modules/Costs/Comuki.Modules.Costs.Application/Comuki.Modules.Costs.Application.csproj',
    );
  });

  it('resolves a forward-slash reference the same way', () => {
    const resolved = resolveProjectReference('a/b/B.csproj', '../../a/c/C.csproj');
    assert.equal(resolved, 'a/c/C.csproj');
  });
});

// ---------------------------------------------------------------------------
// buildProjectGraph / reverseGraph / closureOverReverseGraph
// ---------------------------------------------------------------------------

/** Domain <- Application <- Unit.Test  (Unit.Test depends on Application depends on Domain) */
function fixtureEntries() {
  return [
    { path: 'platform/src/modules/X/Domain/Domain.csproj', content: '<Project></Project>' },
    {
      path: 'platform/src/modules/X/Application/Application.csproj',
      content: `<Project><ItemGroup>
        <ProjectReference Include="..\\Domain\\Domain.csproj" />
      </ItemGroup></Project>`,
    },
    {
      path: 'tests/unit/X.Unit/X.Unit.csproj',
      content: `<Project><ItemGroup>
        <ProjectReference Include="..\\..\\..\\platform\\src\\modules\\X\\Application\\Application.csproj" />
      </ItemGroup></Project>`,
    },
    {
      path: 'tests/integration/X.Integration/X.Integration.csproj',
      content: `<Project><ItemGroup>
        <ProjectReference Include="..\\..\\..\\platform\\src\\modules\\X\\Application\\Application.csproj" />
      </ItemGroup></Project>`,
    },
  ];
}

describe('buildProjectGraph', () => {
  it('builds forward edges from every project, including leaves with none', () => {
    const graph = buildProjectGraph(fixtureEntries());
    assert.deepEqual([...graph.get('platform/src/modules/X/Domain/Domain.csproj')], []);
    assert.deepEqual(
      [...graph.get('platform/src/modules/X/Application/Application.csproj')],
      ['platform/src/modules/X/Domain/Domain.csproj'],
    );
    assert.deepEqual(
      [...graph.get('tests/unit/X.Unit/X.Unit.csproj')],
      ['platform/src/modules/X/Application/Application.csproj'],
    );
  });
});

describe('reverseGraph', () => {
  it('inverts forward edges into dependents', () => {
    const graph = buildProjectGraph(fixtureEntries());
    const rev = reverseGraph(graph);
    assert.deepEqual(
      [...rev.get('platform/src/modules/X/Domain/Domain.csproj')].sort(),
      ['platform/src/modules/X/Application/Application.csproj'],
    );
    assert.deepEqual(
      [...rev.get('platform/src/modules/X/Application/Application.csproj')].sort(),
      ['tests/integration/X.Integration/X.Integration.csproj', 'tests/unit/X.Unit/X.Unit.csproj'],
    );
  });
});

describe('closureOverReverseGraph', () => {
  it('includes the seed plus every transitive dependent (multi-hop: Domain -> Application -> Unit.Test)', () => {
    const graph = buildProjectGraph(fixtureEntries());
    const rev = reverseGraph(graph);
    const closure = closureOverReverseGraph(['platform/src/modules/X/Domain/Domain.csproj'], rev);
    assert.ok(closure.has('platform/src/modules/X/Domain/Domain.csproj'));
    assert.ok(closure.has('platform/src/modules/X/Application/Application.csproj'));
    assert.ok(closure.has('tests/unit/X.Unit/X.Unit.csproj'));
    assert.ok(closure.has('tests/integration/X.Integration/X.Integration.csproj'));
  });

  it('a leaf test project with nothing depending on it closes over just itself', () => {
    const graph = buildProjectGraph(fixtureEntries());
    const rev = reverseGraph(graph);
    const closure = closureOverReverseGraph(['tests/unit/X.Unit/X.Unit.csproj'], rev);
    assert.deepEqual([...closure], ['tests/unit/X.Unit/X.Unit.csproj']);
  });

  it('never loops on a reference cycle', () => {
    const graph = new Map([
      ['A.csproj', new Set(['B.csproj'])],
      ['B.csproj', new Set(['A.csproj'])],
    ]);
    const rev = reverseGraph(graph);
    const closure = closureOverReverseGraph(['A.csproj'], rev);
    assert.deepEqual([...closure].sort(), ['A.csproj', 'B.csproj']);
  });
});

// ---------------------------------------------------------------------------
// findOwningProject
// ---------------------------------------------------------------------------

describe('findOwningProject', () => {
  const dirToCsproj = new Map([
    ['platform/src/modules/X/Domain', 'platform/src/modules/X/Domain/Domain.csproj'],
    ['tests/unit/X.Unit', 'tests/unit/X.Unit/X.Unit.csproj'],
  ]);

  it('resolves a file nested inside the project directory', () => {
    assert.equal(
      findOwningProject('platform/src/modules/X/Domain/Entities/Money.cs', dirToCsproj),
      'platform/src/modules/X/Domain/Domain.csproj',
    );
  });

  it('resolves a file several levels below the project directory', () => {
    assert.equal(
      findOwningProject('platform/src/modules/X/Domain/Entities/Nested/Deep.cs', dirToCsproj),
      'platform/src/modules/X/Domain/Domain.csproj',
    );
  });

  it('resolves the csproj file itself', () => {
    assert.equal(
      findOwningProject('tests/unit/X.Unit/X.Unit.csproj', dirToCsproj),
      'tests/unit/X.Unit/X.Unit.csproj',
    );
  });

  it('returns null when no ancestor directory is a project directory', () => {
    assert.equal(findOwningProject('README.md', dirToCsproj), null);
  });
});

// ---------------------------------------------------------------------------
// classifyProjectPaths
// ---------------------------------------------------------------------------

describe('classifyProjectPaths', () => {
  it('splits unit vs. integration by path prefix', () => {
    const { unit, integration } = classifyProjectPaths([
      'platform/src/modules/X/Domain/Domain.csproj',
      'tests/unit/X.Unit/X.Unit.csproj',
      'tests/integration/X.Integration/X.Integration.csproj',
    ]);
    assert.deepEqual(unit, ['tests/unit/X.Unit/X.Unit.csproj']);
    assert.deepEqual(integration, ['tests/integration/X.Integration/X.Integration.csproj']);
  });

  it('excludes shared test infra (Comuki.Host.Testing) from the integration bucket', () => {
    const { integration } = classifyProjectPaths(
      ['tests/integration/Comuki.Host.Testing/Comuki.Host.Testing.csproj'],
      new Set(['Comuki.Host.Testing']),
    );
    assert.deepEqual(integration, []);
  });
});

// ---------------------------------------------------------------------------
// dashboardVitestMode
// ---------------------------------------------------------------------------

describe('dashboardVitestMode', () => {
  it('uses related mode for plain source/test file changes', () => {
    assert.equal(dashboardVitestMode(['dashboard/src/app/App.tsx', 'dashboard/src/app/App.test.tsx']), 'related');
  });

  it('falls back to full mode when a config file changed', () => {
    assert.equal(dashboardVitestMode(['dashboard/vite.config.ts']), 'full');
    assert.equal(dashboardVitestMode(['dashboard/package.json']), 'full');
    assert.equal(dashboardVitestMode(['dashboard/src/app/App.tsx', 'dashboard/vitest.config.ts']), 'full');
  });

  it('falls back to full mode when there are no changed files', () => {
    assert.equal(dashboardVitestMode([]), 'full');
  });
});

// ---------------------------------------------------------------------------
// parseNodeTestOutput
// ---------------------------------------------------------------------------

describe('parseNodeTestOutput', () => {
  it('returns null when the output has no ℹ tests summary (e.g. a crash before the runner starts)', () => {
    assert.equal(parseNodeTestOutput('SyntaxError: Unexpected token\n'), null);
  });

  it('reads the real counts from a clean pass, with no failures', () => {
    const output = [
      '▶ parseArgs',
      '  ✔ defaults everything to unset/false (1.2ms)',
      '✔ parseArgs (1.5ms)',
      'ℹ tests 3',
      'ℹ suites 1',
      'ℹ pass 3',
      'ℹ fail 0',
      'ℹ cancelled 0',
      'ℹ skipped 0',
      'ℹ todo 0',
      'ℹ duration_ms 12.3',
      '',
    ].join('\n');
    const result = parseNodeTestOutput(output);
    assert.deepEqual(result.counts, { total: 3, passed: 3, failed: 0, skipped: 0 });
    assert.deepEqual(result.failures, []);
  });

  it('extracts location + name + first message line per failing test from the "failing tests:" section', () => {
    const output = [
      '▶ parseArgs',
      '  ✖ defaults everything to unset/false (1.4ms)',
      '✖ parseArgs (3.5ms)',
      'ℹ tests 205',
      'ℹ suites 35',
      'ℹ pass 204',
      'ℹ fail 1',
      'ℹ cancelled 0',
      'ℹ skipped 0',
      'ℹ todo 0',
      'ℹ duration_ms 456',
      '',
      '✖ failing tests:',
      '',
      'test at scripts\\ci\\test-affected.test.mjs:43:3',
      '✖ defaults everything to unset/false (1.4615ms)',
      '  AssertionError [ERR_ASSERTION]: Expected values to be strictly equal:',
      '  ',
      '  false !== true',
      '  ',
      '      at TestContext.<anonymous> (file:///.../test-affected.test.mjs:47:12)',
    ].join('\n');
    const result = parseNodeTestOutput(output);
    assert.deepEqual(result.counts, { total: 205, passed: 204, failed: 1, skipped: 0 });
    assert.equal(result.failures.length, 1);
    assert.equal(result.failures[0].name, 'defaults everything to unset/false');
    assert.equal(result.failures[0].location, 'scripts\\ci\\test-affected.test.mjs:43:3');
    assert.match(result.failures[0].message, /AssertionError \[ERR_ASSERTION\]/);
  });

  it('extracts one failure block per failing test when several fail', () => {
    const output = [
      'ℹ tests 4',
      'ℹ pass 2',
      'ℹ fail 2',
      'ℹ skipped 0',
      'ℹ cancelled 0',
      '',
      '✖ failing tests:',
      '',
      'test at a.test.mjs:1:1',
      '✖ first thing (0.1ms)',
      '  boom one',
      '',
      'test at b.test.mjs:2:2',
      '✖ second thing (0.2ms)',
      '  boom two',
    ].join('\n');
    const result = parseNodeTestOutput(output);
    assert.equal(result.failures.length, 2);
    assert.deepEqual(
      result.failures.map((f) => f.name),
      ['first thing', 'second thing'],
    );
    assert.deepEqual(
      result.failures.map((f) => f.location),
      ['a.test.mjs:1:1', 'b.test.mjs:2:2'],
    );
  });

  it('folds skipped and cancelled together into skipped', () => {
    const output = ['ℹ tests 5', 'ℹ pass 3', 'ℹ fail 0', 'ℹ skipped 1', 'ℹ cancelled 1'].join('\n');
    assert.equal(parseNodeTestOutput(output).counts.skipped, 2);
  });
});

// ---------------------------------------------------------------------------
// openspecItemsFromChangedFiles / summarizeOpenspecValidation
// ---------------------------------------------------------------------------

describe('openspecItemsFromChangedFiles', () => {
  it('maps a changes/ path to a single change item', () => {
    const items = openspecItemsFromChangedFiles([
      'openspec/changes/add-thing/proposal.md',
      'openspec/changes/add-thing/tasks.md',
    ]);
    assert.deepEqual(items, [{ kind: 'change', id: 'add-thing' }]);
  });

  it('maps a specs/ path to a single spec item', () => {
    const items = openspecItemsFromChangedFiles(['openspec/specs/runs/spec.md']);
    assert.deepEqual(items, [{ kind: 'spec', id: 'runs' }]);
  });

  it('dedupes multiple files within the same change/spec', () => {
    const items = openspecItemsFromChangedFiles([
      'openspec/changes/add-thing/tasks.md',
      'openspec/changes/add-thing/design.md',
    ]);
    assert.equal(items.length, 1);
  });

  it('handles multiple distinct changes in one diff', () => {
    const items = openspecItemsFromChangedFiles([
      'openspec/changes/add-thing/proposal.md',
      'openspec/changes/add-other/proposal.md',
    ]);
    assert.deepEqual(
      items.sort((a, b) => a.id.localeCompare(b.id)),
      [
        { kind: 'change', id: 'add-other' },
        { kind: 'change', id: 'add-thing' },
      ],
    );
  });

  it('falls back to a whole-tree item for a top-level openspec file', () => {
    assert.deepEqual(openspecItemsFromChangedFiles(['openspec/config.yaml']), [{ kind: 'all' }]);
  });
});

describe('summarizeOpenspecValidation', () => {
  it('returns null for a shape that is not --json validate output', () => {
    assert.equal(summarizeOpenspecValidation({ foo: 'bar' }, []), null);
    assert.equal(summarizeOpenspecValidation(null, []), null);
  });

  it('reports zero failures when every item is valid', () => {
    const parsed = {
      items: [{ id: 'runs', type: 'spec', valid: true, issues: [] }],
      summary: { totals: { items: 1, passed: 1, failed: 0 } },
    };
    const result = summarizeOpenspecValidation(parsed, ['log.txt']);
    assert.deepEqual(result.counts, { total: 1, passed: 1, failed: 0, skipped: 0 });
    assert.deepEqual(result.failures, []);
  });

  it('extracts the first ERROR-level issue message per invalid item', () => {
    const parsed = {
      items: [
        {
          id: 'add-thing',
          type: 'change',
          valid: false,
          issues: [
            { level: 'WARNING', path: 'spec.md', message: 'should contain SHALL' },
            { level: 'ERROR', path: 'spec.md', message: 'must include a scenario' },
          ],
        },
      ],
      summary: { totals: { items: 1, passed: 0, failed: 1 } },
    };
    const result = summarizeOpenspecValidation(parsed, ['log.txt']);
    assert.deepEqual(result.counts, { total: 1, passed: 0, failed: 1, skipped: 0 });
    assert.equal(result.failures.length, 1);
    assert.equal(result.failures[0].scenario, 'openspec:change:add-thing');
    assert.equal(result.failures[0].message, 'spec.md: must include a scenario');
    assert.deepEqual(result.failures[0].artifactPaths, ['log.txt']);
  });
});

// ---------------------------------------------------------------------------
// buildPlan
// ---------------------------------------------------------------------------

describe('buildPlan', () => {
  const graph = buildProjectGraph(fixtureEntries());

  it('a Domain-layer change surfaces the Unit test project through the multi-hop graph, ' +
     'excludes integration by default, and includes a podman-hint note', () => {
    const plan = buildPlan({
      changedFiles: ['platform/src/modules/X/Domain/Money.cs'],
      projectGraph: graph,
      scriptTestFiles: [],
      includeIntegration: false,
    });
    const dotnetTargets = plan.targets.filter((t) => t.kind === 'dotnet');
    assert.ok(dotnetTargets.some((t) => t.id === 'dotnet:unit:X.Unit'));
    assert.ok(!dotnetTargets.some((t) => t.tier === 'integration'));
    assert.ok(plan.notes.some((n) => n.includes('integration tier skipped')));
    assert.ok(plan.notes.some((n) => n.includes('Podman')));
  });

  it('includes integration targets when --include-integration is set', () => {
    const plan = buildPlan({
      changedFiles: ['platform/src/modules/X/Domain/Money.cs'],
      projectGraph: graph,
      scriptTestFiles: [],
      includeIntegration: true,
    });
    assert.ok(plan.targets.some((t) => t.id === 'dotnet:integration:X.Integration'));
  });

  it('adds Architecture.Tests whenever platform/ changed, regardless of the graph', () => {
    const graphWithArch = new Map(graph);
    graphWithArch.set('tests/Comuki.Architecture.Tests/Comuki.Architecture.Tests.csproj', new Set());
    const plan = buildPlan({
      changedFiles: ['platform/src/modules/X/Domain/Money.cs'],
      projectGraph: graphWithArch,
      scriptTestFiles: [],
    });
    assert.ok(plan.targets.some((t) => t.id === 'dotnet:arch:Comuki.Architecture.Tests'));
  });

  it('does not add Architecture.Tests when it is not present in the discovered graph', () => {
    const plan = buildPlan({
      changedFiles: ['platform/src/modules/X/Domain/Money.cs'],
      projectGraph: graph,
      scriptTestFiles: [],
    });
    assert.ok(!plan.targets.some((t) => t.id.startsWith('dotnet:arch:')));
  });

  it('a dashboard-only source change plans typecheck + lint + vitest related', () => {
    const plan = buildPlan({
      changedFiles: ['dashboard/src/app/App.tsx'],
      projectGraph: new Map(),
      scriptTestFiles: [],
    });
    assert.deepEqual(
      plan.targets.map((t) => t.id),
      ['dashboard:typecheck', 'dashboard:lint', 'dashboard:vitest-related'],
    );
    const related = plan.targets.find((t) => t.id === 'dashboard:vitest-related');
    assert.deepEqual(related.args, ['x', 'vitest', 'related', '--run', 'src/app/App.tsx']);
  });

  it('a dashboard config change falls back to the full vitest run with a note', () => {
    const plan = buildPlan({
      changedFiles: ['dashboard/vite.config.ts'],
      projectGraph: new Map(),
      scriptTestFiles: [],
    });
    assert.ok(plan.targets.some((t) => t.id === 'dashboard:vitest'));
    assert.ok(!plan.targets.some((t) => t.id === 'dashboard:vitest-related'));
    assert.ok(plan.notes.some((n) => n.includes('full vitest suite')));
  });

  it('a cli-only change plans just cli:test', () => {
    const plan = buildPlan({
      changedFiles: ['cli/src/index.ts'],
      projectGraph: new Map(),
      scriptTestFiles: [],
    });
    assert.deepEqual(plan.targets.map((t) => t.id), ['cli:test']);
  });

  it('an agents-only change plans just agents:test', () => {
    const plan = buildPlan({
      changedFiles: ['agents/comuki-worker-sdk/src/index.ts'],
      projectGraph: new Map(),
      scriptTestFiles: [],
    });
    assert.deepEqual(plan.targets.map((t) => t.id), ['agents:test']);
  });

  it('a scripts/ change plans node --test over discovered test files', () => {
    const plan = buildPlan({
      changedFiles: ['scripts/ci/test-affected.mjs'],
      projectGraph: new Map(),
      scriptTestFiles: ['scripts/ci/dotnet-test.test.mjs', 'scripts/ci/test-affected.test.mjs'],
    });
    const target = plan.targets.find((t) => t.id === 'scripts:node-test');
    assert.ok(target);
    assert.deepEqual(target.args, [
      '--test',
      'scripts/ci/dotnet-test.test.mjs',
      'scripts/ci/test-affected.test.mjs',
    ]);
  });

  it('a scripts/ change with no discovered test files notes it and plans nothing', () => {
    const plan = buildPlan({
      changedFiles: ['scripts/hooks/pre-commit'],
      projectGraph: new Map(),
      scriptTestFiles: [],
    });
    assert.ok(!plan.targets.some((t) => t.id === 'scripts:node-test'));
    assert.ok(plan.notes.some((n) => n.includes('no *.test.mjs files found')));
  });

  it('an openspec change plans a scoped validate, not --all', () => {
    const plan = buildPlan({
      changedFiles: ['openspec/changes/add-thing/proposal.md'],
      projectGraph: new Map(),
      scriptTestFiles: [],
    });
    assert.equal(plan.targets.length, 1);
    assert.equal(plan.targets[0].id, 'openspec:change:add-thing');
    assert.deepEqual(plan.targets[0].args, ['validate', 'add-thing', '--type', 'change', '--json']);
  });

  it('a docs-only diff plans nothing and notes it', () => {
    const plan = buildPlan({
      changedFiles: ['README.md', 'DESIGN.md'],
      projectGraph: new Map(),
      scriptTestFiles: [],
    });
    assert.equal(plan.targets.length, 0);
    assert.ok(plan.notes.some((n) => n.includes('docs-only')));
  });

  it('an empty diff also plans nothing', () => {
    const plan = buildPlan({ changedFiles: [], projectGraph: new Map(), scriptTestFiles: [] });
    assert.equal(plan.targets.length, 0);
  });

  it('a mixed .NET + dashboard diff plans both families', () => {
    const plan = buildPlan({
      changedFiles: ['platform/src/modules/X/Domain/Money.cs', 'dashboard/src/app/App.tsx'],
      projectGraph: graph,
      scriptTestFiles: [],
    });
    assert.ok(plan.targets.some((t) => t.kind === 'dotnet'));
    assert.ok(plan.targets.some((t) => t.id === 'dashboard:typecheck'));
  });
});

// ---------------------------------------------------------------------------
// renderPlanText
// ---------------------------------------------------------------------------

describe('renderPlanText', () => {
  it('renders "(nothing to run)" for an empty plan', () => {
    const text = renderPlanText({ targets: [], notes: [] });
    assert.match(text, /0 target\(s\)/);
    assert.match(text, /\(nothing to run\)/);
  });

  it('renders one line per target plus a Notes section', () => {
    const plan = buildPlan({
      changedFiles: ['cli/src/index.ts'],
      projectGraph: new Map(),
      scriptTestFiles: [],
    });
    const text = renderPlanText(plan);
    assert.match(text, /1 target\(s\)/);
    assert.match(text, /\[cli:test\]/);
  });

  it('renders a Notes section when notes are present', () => {
    const plan = buildPlan({ changedFiles: ['README.md'], projectGraph: new Map(), scriptTestFiles: [] });
    const text = renderPlanText(plan);
    assert.match(text, /Notes:/);
    assert.match(text, /docs-only/);
  });
});

// ---------------------------------------------------------------------------
// IO-touching discovery, against a synthetic temp-dir fixture
// (mirrors dotnet-test.test.mjs's approach — never the real repo tree)
// ---------------------------------------------------------------------------

describe('discoverScriptTestFiles + loadProjectGraph (temp-dir fixture)', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'test-affected-fixture-'));

  after(() => {
    rmSync(root, { recursive: true, force: true });
  });

  mkdirSync(path.join(root, 'scripts', 'ci'), { recursive: true });
  mkdirSync(path.join(root, 'scripts', 'hooks'), { recursive: true });
  writeFileSync(path.join(root, 'scripts', 'ci', 'foo.test.mjs'), '// test');
  writeFileSync(path.join(root, 'scripts', 'ci', 'foo.mjs'), '// not a test file');
  writeFileSync(path.join(root, 'scripts', 'hooks', 'ignored.test.mjs'), '// excluded dir');

  mkdirSync(path.join(root, 'platform', 'src', 'shared', 'Comuki.Shared.Kernel'), { recursive: true });
  mkdirSync(path.join(root, 'tests', 'unit', 'Comuki.Shared.Kernel.Tests'), { recursive: true });
  writeFileSync(
    path.join(root, 'platform', 'src', 'shared', 'Comuki.Shared.Kernel', 'Comuki.Shared.Kernel.csproj'),
    '<Project></Project>',
  );
  writeFileSync(
    path.join(root, 'tests', 'unit', 'Comuki.Shared.Kernel.Tests', 'Comuki.Shared.Kernel.Tests.csproj'),
    `<Project><ItemGroup>
      <ProjectReference Include="..\\..\\..\\platform\\src\\shared\\Comuki.Shared.Kernel\\Comuki.Shared.Kernel.csproj" />
    </ItemGroup></Project>`,
  );
  // bin/obj noise that must not be picked up
  mkdirSync(path.join(root, 'tests', 'unit', 'Comuki.Shared.Kernel.Tests', 'bin'), { recursive: true });
  writeFileSync(path.join(root, 'tests', 'unit', 'Comuki.Shared.Kernel.Tests', 'bin', 'Fake.csproj'), '<Project/>');

  it('discovers *.test.mjs under scripts/, excluding scripts/hooks and non-test files', () => {
    const files = discoverScriptTestFiles(root);
    assert.deepEqual(files, ['scripts/ci/foo.test.mjs']);
  });

  it('loads a real forward ProjectReference graph from disk, ignoring bin/obj', () => {
    const graph = loadProjectGraph(root);
    assert.ok(graph.has('platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj'));
    assert.deepEqual(
      [...graph.get('tests/unit/Comuki.Shared.Kernel.Tests/Comuki.Shared.Kernel.Tests.csproj')],
      ['platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj'],
    );
    assert.equal(graph.has('tests/unit/Comuki.Shared.Kernel.Tests/bin/Fake.csproj'), false);
  });

  it('end-to-end: a Domain change in the fixture closes over its Unit test via buildPlan', () => {
    const graph = loadProjectGraph(root);
    const plan = buildPlan({
      changedFiles: ['platform/src/shared/Comuki.Shared.Kernel/Money.cs'],
      projectGraph: graph,
      scriptTestFiles: [],
    });
    assert.ok(plan.targets.some((t) => t.id === 'dotnet:unit:Comuki.Shared.Kernel.Tests'));
  });
});
