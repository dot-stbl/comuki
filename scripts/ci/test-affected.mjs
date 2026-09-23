#!/usr/bin/env node
/**
 * scripts/ci/test-affected.mjs — WS18 of add-agentic-test-contour: the one
 * command a coding agent (or a human) runs before opening an MR to find out
 * which tests a change actually needs, run them once, and get back a single
 * report an agent can act on without re-running anything.
 *
 *   node scripts/ci/test-affected.mjs
 *   node scripts/ci/test-affected.mjs --dry-run
 *   node scripts/ci/test-affected.mjs --base origin/master
 *   node scripts/ci/test-affected.mjs --include-integration
 *   node scripts/ci/test-affected.mjs --report-dir artifacts/test-reports/affected
 *
 * Change classification (git diff vs merge-base → affected targets):
 *
 *   - `.cs` / `.csproj` anywhere under `platform/`, `tests/`, `tools/` →
 *     owning project → reverse ProjectReference closure (parsed straight
 *     from the .csproj files, no `dotnet` invocation needed to know this)
 *     → affected `tests/unit/*` projects. `tests/Comuki.Architecture.Tests`
 *     always runs when anything under `platform/` changed (layer rules are
 *     solution-wide, not traceable through one project's reference edges).
 *     Affected `tests/integration/*` projects are only *executed* with
 *     `--include-integration` (Testcontainers/Podman needed); without the
 *     flag they're named in the plan/notes so nothing is silently skipped.
 *   - `dashboard/**` → typecheck + lint + `vitest related` (falls back to
 *     the full `vitest run` when a config file changed, since "related"
 *     can't reason about those).
 *   - `cli/**` / `agents/**` → that package's own `test` script.
 *   - `scripts/**` → `node --test` over every `*.test.mjs` under `scripts/`.
 *   - `openspec/**` → `openspec validate` scoped to the touched change(s)
 *     / spec(s) (never `--all` — this repo carries pre-existing invalid
 *     changes that would fail every unrelated diff).
 *   - Anything else (docs, root-level config, …) → no targets. A diff that
 *     touches nothing test-relevant reports `PASS 0/0`, not an error.
 *
 * Execution is single-shot and sequential (same spirit as dotnet-test.mjs:
 * no watch mode, one deterministic pass), and reuses dotnet-test.mjs's own
 * `runProject` (dotnet run + CTRF parse) for the .NET side rather than
 * re-implementing it — see the imports below.
 *
 * Report: one envelope, written as report.json + report.md in the shape
 * documented in openspec/changes/add-agentic-test-contour/design.md
 * ("Report format for agents"), built via the same `buildEnvelope` /
 * `renderMarkdown` dotnet-test.mjs uses, so the two commands can never
 * drift on what the envelope looks like. Two fields are appended beyond
 * that shape — `notes` (skipped-integration / podman-hint / docs-only
 * messages) and `targets` (per-target pass/fail counts) — both additive,
 * neither renamed nor removed from the documented envelope.
 *
 * Zero third-party dependencies, same reasoning as dotnet-test.mjs and
 * scripts/commit-lint.mjs: has to run on a bare checkout with nothing
 * installed but node + the dotnet SDK (+ bun for the FE/TS targets it
 * dispatches to, + the `openspec` CLI for the openspec/ target).
 */
import { spawnSync } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  writeFileSync,
} from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import {
  buildEnvelope,
  formatVerdict,
  INTEGRATION_EXCLUDE,
  renderMarkdown,
  runProject,
} from './dotnet-test.mjs';

const __filename = fileURLToPath(import.meta.url);
const REPO_ROOT = path.resolve(path.dirname(__filename), '..', '..');

// ---------------------------------------------------------------------------
// Contract
// ---------------------------------------------------------------------------

/** Top-level dirs that can contain a .csproj — scanning only these keeps
 * discovery cheap and avoids ever walking dashboard/cli/agents node_modules. */
const DOTNET_ROOTS = ['platform', 'tests', 'tools'];

/** `tests/Comuki.Architecture.Tests` always runs when `platform/` changes —
 * see the module doc comment above for why this isn't graph-derived. */
const ARCH_PROJECT_PATH = 'tests/Comuki.Architecture.Tests/Comuki.Architecture.Tests.csproj';

const PODMAN_HINT =
  'Integration tier needs Testcontainers pointed at Podman (Windows/WSL): ' +
  'export DOCKER_HOST=npipe://./pipe/podman-machine-default and ' +
  'TESTCONTAINERS_RYUK_DISABLED=true — see scripts/ci/README.md / design.md "Local dev commands".';

const USAGE = `Usage: node scripts/ci/test-affected.mjs [options]

Classifies the diff against a merge-base into affected test targets, runs
them once, and writes report.json + report.md in the shape documented in
openspec/changes/add-agentic-test-contour/design.md ("Report format for
agents").

Options:
  --base <ref>            Ref to diff against (merge-base of <ref> and HEAD).
                           Default: origin/master if it resolves, else master.
  --dry-run                Print the plan and exit 0 — nothing is run, no
                           report is written.
  --include-integration    Also execute affected tests/integration/* projects
                           (needs a working Docker/Podman socket). Without
                           this flag they're named in the plan but not run.
  --report-dir <dir>       Where to write report.json/report.md and per-target
                           artifacts. Default: artifacts/test-reports/affected.
  --help, -h               Show this help and exit.
`;

// ---------------------------------------------------------------------------
// Argument parsing
// ---------------------------------------------------------------------------

/**
 * @param {string[]} argv
 * @returns {{ ok: true, options: object } | { ok: false, error: string }}
 */
export function parseArgs(argv) {
  const options = {
    base: undefined,
    dryRun: false,
    includeIntegration: false,
    reportDir: undefined,
    help: false,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    if (raw === '--help' || raw === '-h') {
      options.help = true;
      continue;
    }
    if (raw === '--dry-run') {
      options.dryRun = true;
      continue;
    }
    if (raw === '--include-integration') {
      options.includeIntegration = true;
      continue;
    }

    const eq = raw.indexOf('=');
    const flag = eq === -1 ? raw : raw.slice(0, eq);
    const inlineValue = eq === -1 ? undefined : raw.slice(eq + 1);
    const takeValue = () => {
      if (inlineValue !== undefined) {
        return inlineValue;
      }
      const next = argv[i + 1];
      if (next === undefined || next.startsWith('--')) {
        return undefined;
      }
      i += 1;
      return next;
    };

    if (flag === '--base') {
      const value = takeValue();
      if (value === undefined) {
        return { ok: false, error: '--base needs a ref, e.g. --base origin/master' };
      }
      options.base = value;
      continue;
    }
    if (flag === '--report-dir') {
      const value = takeValue();
      if (value === undefined) {
        return { ok: false, error: '--report-dir needs a path' };
      }
      options.reportDir = value;
      continue;
    }

    return { ok: false, error: `unrecognized argument: ${raw}` };
  }

  return { ok: true, options };
}

// ---------------------------------------------------------------------------
// Small shared helpers
// ---------------------------------------------------------------------------

/** Force forward slashes, so plan/report content is stable regardless of
 * whether this ran on Windows or Linux CI. */
function toPosixPath(p) {
  return p.split(path.sep).join('/');
}

function firstNonEmptyLine(text) {
  const line = (text ?? '').split(/\r?\n/).find((l) => l.trim().length > 0);
  return line ? line.trim() : '';
}

function sanitizeId(id) {
  return id.replace(/[^a-zA-Z0-9._-]+/g, '-');
}

// ---------------------------------------------------------------------------
// Git — base ref resolution + changed-file discovery
// ---------------------------------------------------------------------------

/**
 * Pick the ref to diff against: an explicit `--base` wins; otherwise prefer
 * `origin/master` when it resolves (matches CI, which always has it), else
 * fall back to local `master`.
 * @param {string | undefined} explicitBase
 * @param {(ref: string) => boolean} verifyRefExists
 * @returns {string}
 */
export function resolveBaseRef(explicitBase, verifyRefExists) {
  if (explicitBase) {
    return explicitBase;
  }
  if (verifyRefExists('origin/master')) {
    return 'origin/master';
  }
  return 'master';
}

function verifyRefExists(ref) {
  const result = spawnSync('git', ['rev-parse', '--verify', '--quiet', ref], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
  });
  return result.status === 0;
}

/**
 * Changed files = tracked diff of the working tree against the merge-base
 * (covers committed *and* uncommitted edits — the loop this is built for is
 * "agent mid-session, hasn't committed yet") plus untracked new files.
 * @param {string} baseRef
 * @returns {{ ok: true, mergeBase: string, files: string[] } | { ok: false, error: string }}
 */
function getChangedFilesFromGit(baseRef) {
  const mergeBaseResult = spawnSync('git', ['merge-base', baseRef, 'HEAD'], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
  });
  if (mergeBaseResult.status !== 0) {
    return {
      ok: false,
      error: `git merge-base ${baseRef} HEAD failed: ${(mergeBaseResult.stderr ?? '').trim()}`,
    };
  }
  const mergeBase = mergeBaseResult.stdout.trim();

  const diffResult = spawnSync('git', ['diff', '--name-only', mergeBase], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
  });
  if (diffResult.status !== 0) {
    return {
      ok: false,
      error: `git diff --name-only ${mergeBase} failed: ${(diffResult.stderr ?? '').trim()}`,
    };
  }
  const tracked = diffResult.stdout
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean);

  const statusResult = spawnSync(
    'git',
    ['status', '--porcelain=v1', '--untracked-files=all'],
    { cwd: REPO_ROOT, encoding: 'utf8' },
  );
  const untracked =
    statusResult.status === 0
      ? statusResult.stdout
          .split('\n')
          .filter((l) => l.startsWith('??'))
          .map((l) => l.slice(3).trim())
          .filter(Boolean)
      : [];

  const files = [...new Set([...tracked, ...untracked])].map(toPosixPath).sort();
  return { ok: true, mergeBase, files };
}

// ---------------------------------------------------------------------------
// Change classification
// ---------------------------------------------------------------------------

/**
 * @typedef {{
 *   dotnet: string[], dashboard: string[], cli: string[], agentsPkg: string[],
 *   scripts: string[], openspec: string[], other: string[],
 * }} ClassifiedFiles
 */

/**
 * Bucket changed (repo-relative, POSIX) paths by which target family owns
 * them. `other` is everything that isn't test-relevant on its own (docs,
 * root config, deploy/, control-plane/, …) — a diff whose files are *all*
 * `other` needs no targets at all.
 * @param {readonly string[]} files
 * @returns {ClassifiedFiles}
 */
export function classifyChangedFiles(files) {
  const dotnet = [];
  const dashboard = [];
  const cli = [];
  const agentsPkg = [];
  const scripts = [];
  const openspec = [];
  const other = [];

  for (const f of files) {
    if (f.startsWith('dashboard/')) {
      dashboard.push(f);
    } else if (f.startsWith('cli/')) {
      cli.push(f);
    } else if (f.startsWith('agents/')) {
      agentsPkg.push(f);
    } else if (f.startsWith('scripts/')) {
      scripts.push(f);
    } else if (f.startsWith('openspec/')) {
      openspec.push(f);
    } else if (/\.(cs|csproj)$/i.test(f)) {
      dotnet.push(f);
    } else {
      other.push(f);
    }
  }

  return { dotnet, dashboard, cli, agentsPkg, scripts, openspec, other };
}

// ---------------------------------------------------------------------------
// .csproj discovery + ProjectReference graph
// ---------------------------------------------------------------------------

/**
 * Extract every `<ProjectReference Include="...">` target from a .csproj's
 * raw XML text — regex on purpose, same reasoning as dotnet-test.mjs's CTRF
 * choice not to grep human output: this only needs one attribute value, not
 * a full XML parser dependency, and it's robust to both the self-closing
 * and open/close tag forms MSBuild accepts.
 * @param {string} csprojXmlText
 * @returns {string[]} raw `Include` values, unresolved (may use `\`)
 */
export function parseProjectReferences(csprojXmlText) {
  const matches = [...csprojXmlText.matchAll(/<ProjectReference\s+Include\s*=\s*"([^"]+)"/gi)];
  return matches.map((m) => m[1]);
}

/**
 * Resolve one raw `ProjectReference/@Include` value (relative, `\`- or
 * `/`-separated) against the referencing .csproj's own repo-relative POSIX
 * path, into a repo-relative POSIX path.
 * @param {string} csprojPosixPath
 * @param {string} rawRef
 * @returns {string}
 */
export function resolveProjectReference(csprojPosixPath, rawRef) {
  const csprojDir = path.posix.dirname(csprojPosixPath);
  const rawPosix = rawRef.replace(/\\/g, '/');
  return path.posix.normalize(path.posix.join(csprojDir, rawPosix));
}

/**
 * Build the forward ProjectReference graph from parsed csproj entries.
 * Pure — takes text, not paths, so it's testable against a synthetic
 * fixture without touching the real filesystem.
 * @param {{ path: string, content: string }[]} entries
 * @returns {Map<string, Set<string>>} project path → set of paths it references
 */
export function buildProjectGraph(entries) {
  const graph = new Map();
  for (const e of entries) {
    graph.set(e.path, new Set());
  }
  for (const e of entries) {
    for (const raw of parseProjectReferences(e.content)) {
      graph.get(e.path).add(resolveProjectReference(e.path, raw));
    }
  }
  return graph;
}

/**
 * Invert a forward graph: project path → set of paths that reference it.
 * @param {Map<string, Set<string>>} graph
 * @returns {Map<string, Set<string>>}
 */
export function reverseGraph(graph) {
  const rev = new Map();
  for (const p of graph.keys()) {
    rev.set(p, new Set());
  }
  for (const [p, refs] of graph) {
    for (const r of refs) {
      if (!rev.has(r)) {
        rev.set(r, new Set());
      }
      rev.get(r).add(p);
    }
  }
  return rev;
}

/**
 * BFS over the reverse graph from a set of seed projects, returning the
 * seeds themselves plus every (transitive) dependent. Cycle-safe (a
 * ProjectReference cycle shouldn't exist, but this never loops if one did).
 * @param {Iterable<string>} seeds
 * @param {Map<string, Set<string>>} reverseGraphMap
 * @returns {Set<string>}
 */
export function closureOverReverseGraph(seeds, reverseGraphMap) {
  const visited = new Set();
  const queue = [...seeds];
  while (queue.length > 0) {
    const current = queue.shift();
    if (visited.has(current)) {
      continue;
    }
    visited.add(current);
    const dependents = reverseGraphMap.get(current);
    if (dependents) {
      for (const d of dependents) {
        if (!visited.has(d)) {
          queue.push(d);
        }
      }
    }
  }
  return visited;
}

/**
 * Find the project that owns a changed file: the nearest ancestor directory
 * that is itself a project directory (walks up from the file's own dir).
 * @param {string} changedFilePosix
 * @param {Map<string, string>} dirToCsprojPath directory → csproj path, from `buildDirIndex`
 * @returns {string | null}
 */
export function findOwningProject(changedFilePosix, dirToCsprojPath) {
  let dir = path.posix.dirname(changedFilePosix);
  while (true) {
    const hit = dirToCsprojPath.get(dir);
    if (hit) {
      return hit;
    }
    const parent = path.posix.dirname(dir);
    if (parent === dir) {
      return null;
    }
    dir = parent;
  }
}

/** @param {Map<string, Set<string>>} graph @returns {Map<string, string>} */
function buildDirIndex(graph) {
  const dirToCsproj = new Map();
  for (const p of graph.keys()) {
    dirToCsproj.set(path.posix.dirname(p), p);
  }
  return dirToCsproj;
}

/**
 * Split an affected-project closure into the unit/integration test projects
 * within it (non-test projects in the closure are just dependency plumbing,
 * not something to run). Applies the same shared-infra exclusion
 * dotnet-test.mjs uses for `--tier=integration`.
 * @param {Iterable<string>} affectedPaths
 * @param {Set<string>} integrationExclude
 * @returns {{ unit: string[], integration: string[] }}
 */
export function classifyProjectPaths(affectedPaths, integrationExclude = INTEGRATION_EXCLUDE) {
  const unit = [];
  const integration = [];
  for (const p of affectedPaths) {
    if (p.startsWith('tests/unit/')) {
      unit.push(p);
    } else if (p.startsWith('tests/integration/')) {
      const name = path.posix.basename(p, '.csproj');
      if (!integrationExclude.has(name)) {
        integration.push(p);
      }
    }
  }
  unit.sort();
  integration.sort();
  return { unit, integration };
}

function discoverCsprojFiles(repoRoot) {
  const results = [];
  function walk(dirPosix) {
    const abs = path.join(repoRoot, ...dirPosix.split('/'));
    let entries;
    try {
      entries = readdirSync(abs, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      if (entry.isDirectory()) {
        if (entry.name === 'bin' || entry.name === 'obj' || entry.name === 'node_modules') {
          continue;
        }
        walk(`${dirPosix}/${entry.name}`);
      } else if (entry.isFile() && entry.name.endsWith('.csproj')) {
        results.push(`${dirPosix}/${entry.name}`);
      }
    }
  }
  for (const root of DOTNET_ROOTS) {
    walk(root);
  }
  return results.sort();
}

/** IO wrapper: discover every .csproj under the repo's dotnet roots and
 * build the forward ProjectReference graph from their real contents. */
export function loadProjectGraph(repoRoot) {
  const csprojPaths = discoverCsprojFiles(repoRoot);
  const entries = csprojPaths.map((p) => ({
    path: p,
    content: readFileSync(path.join(repoRoot, ...p.split('/')), 'utf8'),
  }));
  return buildProjectGraph(entries);
}

// ---------------------------------------------------------------------------
// scripts/ — node --test discovery
// ---------------------------------------------------------------------------

/** IO: every `*.test.mjs` under `scripts/`, recursively (excludes
 * scripts/hooks, which is shell, not JS). */
export function discoverScriptTestFiles(repoRoot, scriptsRoot = 'scripts') {
  const results = [];
  function walk(dirPosix) {
    const abs = path.join(repoRoot, ...dirPosix.split('/'));
    let entries;
    try {
      entries = readdirSync(abs, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      if (entry.isDirectory()) {
        if (entry.name === 'node_modules' || entry.name === 'hooks') {
          continue;
        }
        walk(`${dirPosix}/${entry.name}`);
      } else if (entry.isFile() && entry.name.endsWith('.test.mjs')) {
        results.push(`${dirPosix}/${entry.name}`);
      }
    }
  }
  walk(scriptsRoot);
  return results.sort();
}

// ---------------------------------------------------------------------------
// dashboard/ — vitest related vs. full run
// ---------------------------------------------------------------------------

const DASHBOARD_CONFIG_LIKE =
  /(^|\/)(package\.json|vite\.config\.\w+|vitest\.config\.\w+|vitest\.setup\.\w+|tsconfig[^/]*\.json|eslint\.config\.\w+)$/;

/**
 * `vitest related` needs concrete source/test files to reason about; a
 * config-file change invalidates that reasoning (it can affect anything),
 * so fall back to the full suite.
 * @param {readonly string[]} dashboardFiles repo-relative, `dashboard/`-prefixed
 * @returns {'related' | 'full'}
 */
export function dashboardVitestMode(dashboardFiles) {
  if (dashboardFiles.length === 0) {
    return 'full';
  }
  return dashboardFiles.every((f) => !DASHBOARD_CONFIG_LIKE.test(f)) ? 'related' : 'full';
}

// ---------------------------------------------------------------------------
// openspec/ — validate the touched change(s)/spec(s), never --all
// ---------------------------------------------------------------------------

/**
 * @typedef {{ kind: 'change' | 'spec', id: string } | { kind: 'all' }} OpenspecItem
 */

/**
 * Map changed `openspec/**` paths to the specific change(s)/spec(s) to
 * validate. A path outside `changes/<id>/` or `specs/<capability>/` (e.g.
 * `openspec/config.yaml`) falls back to a single whole-tree item.
 * @param {readonly string[]} openspecFiles
 * @returns {OpenspecItem[]}
 */
export function openspecItemsFromChangedFiles(openspecFiles) {
  const items = new Map();
  for (const f of openspecFiles) {
    const changeMatch = /^openspec\/changes\/([^/]+)\//.exec(f);
    if (changeMatch) {
      items.set(`change:${changeMatch[1]}`, { kind: 'change', id: changeMatch[1] });
      continue;
    }
    const specMatch = /^openspec\/specs\/([^/]+)\//.exec(f);
    if (specMatch) {
      items.set(`spec:${specMatch[1]}`, { kind: 'spec', id: specMatch[1] });
      continue;
    }
    items.set('all', { kind: 'all' });
  }
  return [...items.values()];
}

/**
 * Reduce `openspec validate --json`'s parsed output to the counts/failures
 * shape this script aggregates. Returns `null` when the shape isn't the one
 * `--json` produces (caller falls back to exit-code-only handling).
 * @param {unknown} parsed
 * @param {string[]} artifactPaths
 */
export function summarizeOpenspecValidation(parsed, artifactPaths) {
  const totals = parsed?.summary?.totals;
  if (!totals) {
    return null;
  }
  const failures = [];
  for (const item of parsed.items ?? []) {
    if (item.valid === false) {
      const errorMessages = (item.issues ?? [])
        .filter((i) => i.level === 'ERROR')
        .map((i) => `${i.path}: ${i.message}`);
      failures.push({
        scenario: `openspec:${item.type}:${item.id}`,
        stage: 'validate',
        message: errorMessages[0] ?? 'validation failed',
        artifactPaths,
      });
    }
  }
  return {
    counts: { total: totals.items, passed: totals.passed, failed: totals.failed, skipped: 0 },
    failures,
  };
}

// ---------------------------------------------------------------------------
// Plan building (pure — all IO is done by the caller and passed in)
// ---------------------------------------------------------------------------

/**
 * @typedef {
 *   | { kind: 'dotnet', id: string, tier: 'unit' | 'integration' | 'arch', project: { name: string, dirPosix: string } }
 *   | { kind: 'shell', id: string, cmd: string, args: string[], cwdRelative: string }
 *   | { kind: 'openspec', id: string, args: string[] }
 * } Target
 */

function dotnetTarget(csprojPosixPath, tier) {
  const name = path.posix.basename(csprojPosixPath, '.csproj');
  const dirPosix = path.posix.dirname(csprojPosixPath);
  return { kind: 'dotnet', id: `dotnet:${tier}:${name}`, tier, project: { name, dirPosix } };
}

function shellTarget(id, cmd, args, cwdRelative) {
  return { kind: 'shell', id, cmd, args, cwdRelative };
}

function openspecTarget(item) {
  if (item.kind === 'all') {
    return { kind: 'openspec', id: 'openspec:all', args: ['validate', '--all', '--json'] };
  }
  return {
    kind: 'openspec',
    id: `openspec:${item.kind}:${item.id}`,
    args: ['validate', item.id, '--type', item.kind, '--json'],
  };
}

/**
 * @param {{
 *   changedFiles: readonly string[],
 *   projectGraph: Map<string, Set<string>>,
 *   scriptTestFiles: readonly string[],
 *   includeIntegration?: boolean,
 * }} input
 * @returns {{ targets: Target[], notes: string[], classified: ClassifiedFiles }}
 */
export function buildPlan({ changedFiles, projectGraph, scriptTestFiles, includeIntegration = false }) {
  const classified = classifyChangedFiles(changedFiles);
  const targets = [];
  const notes = [];

  // ---- .NET: owning project → reverse ProjectReference closure ----
  if (classified.dotnet.length > 0) {
    const dirToCsproj = buildDirIndex(projectGraph);
    const reverse = reverseGraph(projectGraph);
    const seeds = new Set();
    for (const f of classified.dotnet) {
      const owner = findOwningProject(f, dirToCsproj);
      if (owner) {
        seeds.add(owner);
      } else {
        notes.push(`no owning .csproj found for changed file: ${f}`);
      }
    }
    const affected = closureOverReverseGraph(seeds, reverse);
    const { unit, integration } = classifyProjectPaths(affected);

    for (const p of unit) {
      targets.push(dotnetTarget(p, 'unit'));
    }

    const platformTouched = classified.dotnet.some((f) => f.startsWith('platform/'));
    if (platformTouched && projectGraph.has(ARCH_PROJECT_PATH)) {
      targets.push(dotnetTarget(ARCH_PROJECT_PATH, 'arch'));
    }

    if (integration.length > 0) {
      const names = integration.map((p) => path.posix.basename(p, '.csproj'));
      if (includeIntegration) {
        for (const p of integration) {
          targets.push(dotnetTarget(p, 'integration'));
        }
        notes.push(PODMAN_HINT);
      } else {
        notes.push(
          `integration tier skipped (${names.length} project(s): ${names.join(', ')}) — ` +
            `rerun with --include-integration. ${PODMAN_HINT}`,
        );
      }
    }
  }

  // ---- dashboard/ ----
  if (classified.dashboard.length > 0) {
    targets.push(shellTarget('dashboard:typecheck', 'bun', ['run', 'typecheck'], 'dashboard'));
    targets.push(shellTarget('dashboard:lint', 'bun', ['run', 'lint'], 'dashboard'));
    const mode = dashboardVitestMode(classified.dashboard);
    if (mode === 'related') {
      const rel = classified.dashboard.map((f) => f.slice('dashboard/'.length));
      targets.push(
        shellTarget('dashboard:vitest-related', 'bun', ['x', 'vitest', 'related', '--run', ...rel], 'dashboard'),
      );
    } else {
      targets.push(shellTarget('dashboard:vitest', 'bun', ['run', 'test'], 'dashboard'));
      notes.push('dashboard: config/non-source file changed — ran the full vitest suite instead of `vitest related`');
    }
  }

  // ---- cli/ ----
  if (classified.cli.length > 0) {
    targets.push(shellTarget('cli:test', 'bun', ['run', 'test'], 'cli'));
  }

  // ---- agents/ ----
  if (classified.agentsPkg.length > 0) {
    targets.push(shellTarget('agents:test', 'bun', ['run', 'test'], 'agents'));
  }

  // ---- scripts/ ----
  if (classified.scripts.length > 0) {
    if (scriptTestFiles.length > 0) {
      targets.push(shellTarget('scripts:node-test', 'node', ['--test', ...scriptTestFiles], '.'));
    } else {
      notes.push('scripts/ changed but no *.test.mjs files found under scripts/ — nothing to run');
    }
  }

  // ---- openspec/ ----
  for (const item of openspecItemsFromChangedFiles(classified.openspec)) {
    targets.push(openspecTarget(item));
  }

  if (targets.length === 0) {
    notes.push('no test-relevant changes detected (docs-only or repo-config-only diff)');
  }

  return { targets, notes, classified };
}

// ---------------------------------------------------------------------------
// Dry-run plan rendering
// ---------------------------------------------------------------------------

function describeTarget(t) {
  if (t.kind === 'dotnet') {
    return `[dotnet:${t.tier}] ${t.project.name}`;
  }
  if (t.kind === 'shell') {
    return `[${t.id}] ${t.cmd} ${t.args.join(' ')} (cwd: ${t.cwdRelative})`;
  }
  if (t.kind === 'openspec') {
    return `[${t.id}] openspec ${t.args.join(' ')}`;
  }
  return `[${t.id}]`;
}

/** @param {{ targets: Target[], notes: string[] }} plan */
export function renderPlanText(plan) {
  const lines = [`test:affected plan — ${plan.targets.length} target(s)`, ''];
  if (plan.targets.length === 0) {
    lines.push('(nothing to run)');
  } else {
    for (const t of plan.targets) {
      lines.push(`- ${describeTarget(t)}`);
    }
  }
  if (plan.notes.length > 0) {
    lines.push('', 'Notes:');
    for (const n of plan.notes) {
      lines.push(`- ${n}`);
    }
  }
  return `${lines.join('\n')}\n`;
}

// ---------------------------------------------------------------------------
// Execution
// ---------------------------------------------------------------------------

function runDotnetTarget(t, repoRoot, reportDir) {
  const dirAbs = path.join(repoRoot, ...t.project.dirPosix.split('/'));
  const csprojAbs = path.join(dirAbs, `${t.project.name}.csproj`);
  if (!existsSync(csprojAbs)) {
    return {
      counts: { total: 1, passed: 0, failed: 1, skipped: 0 },
      failures: [
        {
          scenario: t.project.name,
          stage: 'discovery',
          message: `missing ${t.project.dirPosix}/${t.project.name}.csproj`,
          artifactPaths: [],
        },
      ],
    };
  }
  const project = { name: t.project.name, dir: dirAbs, csproj: csprojAbs };
  const dotnetReportDir = path.join(reportDir, 'dotnet', t.tier);
  const result = runProject(project, dotnetReportDir);
  return { counts: result.counts, failures: result.failures };
}

function runShellTarget(t, repoRoot, reportDir) {
  const cwd = path.join(repoRoot, ...t.cwdRelative.split('/'));
  const logDir = path.join(reportDir, 'logs');
  mkdirSync(logDir, { recursive: true });
  const logPath = path.join(logDir, `${sanitizeId(t.id)}.log`);
  const proc = spawnSync(t.cmd, t.args, { cwd, encoding: 'utf8' });
  const combined = `${proc.stdout ?? ''}${proc.stderr ?? ''}`;
  writeFileSync(logPath, combined);
  process.stdout.write(combined);
  const relLog = toPosixPath(path.relative(repoRoot, logPath));

  if (proc.status === 0) {
    return { counts: { total: 1, passed: 1, failed: 0, skipped: 0 }, failures: [] };
  }
  const message =
    firstNonEmptyLine(combined) ||
    `${t.cmd} ${t.args.join(' ')} exited ${proc.status ?? proc.error?.message ?? 'unknown'}`;
  return {
    counts: { total: 1, passed: 0, failed: 1, skipped: 0 },
    failures: [{ scenario: t.id, stage: 'run', message, artifactPaths: [relLog] }],
  };
}

function runOpenspecTarget(t, repoRoot, reportDir) {
  const logDir = path.join(reportDir, 'logs');
  mkdirSync(logDir, { recursive: true });
  const logPath = path.join(logDir, `${sanitizeId(t.id)}.log`);
  const proc = spawnSync('openspec', t.args, { cwd: repoRoot, encoding: 'utf8' });
  const combined = `${proc.stdout ?? ''}${proc.stderr ?? ''}`;
  writeFileSync(logPath, combined);
  const relLog = toPosixPath(path.relative(repoRoot, logPath));

  let parsed = null;
  try {
    parsed = JSON.parse(proc.stdout);
  } catch {
    parsed = null;
  }
  const summarized = summarizeOpenspecValidation(parsed, [relLog]);
  if (summarized) {
    return summarized;
  }

  if (proc.status === 0) {
    return { counts: { total: 1, passed: 1, failed: 0, skipped: 0 }, failures: [] };
  }
  const message = firstNonEmptyLine(combined) || `openspec ${t.args.join(' ')} exited ${proc.status ?? 'unknown'}`;
  return {
    counts: { total: 1, passed: 0, failed: 1, skipped: 0 },
    failures: [{ scenario: t.id, stage: 'run', message, artifactPaths: [relLog] }],
  };
}

/**
 * Run every target in the plan, sequentially, single-shot. Returns the
 * aggregate counts/failures plus a per-target breakdown for the report's
 * `targets` field.
 */
export function runPlan(plan, { repoRoot, reportDir }) {
  mkdirSync(reportDir, { recursive: true });
  let total = 0;
  let passed = 0;
  let failed = 0;
  let skipped = 0;
  const failures = [];
  const targetResults = [];

  for (const t of plan.targets) {
    process.stdout.write(`--- ${describeTarget(t)} ---\n`);
    let result;
    if (t.kind === 'dotnet') {
      result = runDotnetTarget(t, repoRoot, reportDir);
    } else if (t.kind === 'shell') {
      result = runShellTarget(t, repoRoot, reportDir);
    } else {
      result = runOpenspecTarget(t, repoRoot, reportDir);
    }
    total += result.counts.total;
    passed += result.counts.passed;
    failed += result.counts.failed;
    skipped += result.counts.skipped;
    failures.push(...result.failures);
    targetResults.push({ id: t.id, ...result.counts });
    process.stdout.write(
      `[${t.id}] ${result.counts.passed}/${result.counts.total} passed` +
        `${result.failures.length > 0 ? ` — ${result.failures.length} FAILED` : ''}\n`,
    );
  }

  return { summary: { total, passed, failed, skipped }, failures, targetResults };
}

// ---------------------------------------------------------------------------
// main
// ---------------------------------------------------------------------------

export function main(argv) {
  const parsed = parseArgs(argv);
  if (!parsed.ok) {
    process.stderr.write(`${parsed.error}\n\n${USAGE}`);
    return 1;
  }
  const { options } = parsed;
  if (options.help) {
    process.stdout.write(USAGE);
    return 0;
  }

  const baseRef = resolveBaseRef(options.base, verifyRefExists);
  const diff = getChangedFilesFromGit(baseRef);
  if (!diff.ok) {
    process.stderr.write(`${diff.error}\n`);
    return 1;
  }

  process.stdout.write(
    `test:affected: base=${baseRef} merge-base=${diff.mergeBase.slice(0, 12)} changed=${diff.files.length}\n`,
  );

  const projectGraph = loadProjectGraph(REPO_ROOT);
  const scriptTestFiles = discoverScriptTestFiles(REPO_ROOT);

  const plan = buildPlan({
    changedFiles: diff.files,
    projectGraph,
    scriptTestFiles,
    includeIntegration: options.includeIntegration,
  });

  process.stdout.write(renderPlanText(plan));

  if (options.dryRun) {
    return 0;
  }

  const reportDir = path.isAbsolute(options.reportDir ?? '')
    ? options.reportDir
    : path.join(REPO_ROOT, options.reportDir ?? path.join('artifacts', 'test-reports', 'affected'));

  const startedAtIso = new Date().toISOString();
  const overallStart = Date.now();
  const { summary, failures, targetResults } = runPlan(plan, { repoRoot: REPO_ROOT, reportDir });
  const durationMs = Date.now() - overallStart;

  const report = {
    ...buildEnvelope({ tier: 'affected', startedAt: startedAtIso, durationMs, summary, failures }),
    notes: plan.notes,
    targets: targetResults,
  };

  mkdirSync(reportDir, { recursive: true });
  writeFileSync(path.join(reportDir, 'report.json'), `${JSON.stringify(report, null, 2)}\n`);
  writeFileSync(path.join(reportDir, 'report.md'), renderMarkdown(report));

  process.stdout.write(`\n${formatVerdict(report)}\n`);
  process.stdout.write(
    `report: ${toPosixPath(path.relative(REPO_ROOT, path.join(reportDir, 'report.json')))} / ` +
      `${toPosixPath(path.relative(REPO_ROOT, path.join(reportDir, 'report.md')))}\n`,
  );

  return summary.failed > 0 ? 1 : 0;
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  process.exitCode = main(process.argv.slice(2));
}
