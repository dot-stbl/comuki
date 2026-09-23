#!/usr/bin/env node
/**
 * scripts/ci/dotnet-test.mjs — the one place that knows how to discover and
 * run Comuki's xUnit v3 / Microsoft Testing Platform (MTP) suites, and the
 * one place that renders the JSON+markdown report shape an agent (or a
 * human skimming CI output) reads.
 *
 * xUnit v3 = MTP: `dotnet test` cannot discover it, so every suite runs via
 * `dotnet run --project <csproj>` (see AGENTS.md, critical pattern #4).
 * This script replaces the hand-rolled project lists/loops that used to
 * live separately in .github/workflows/ci.yml and deploy/hybrid/ci.yml.
 *
 *   node scripts/ci/dotnet-test.mjs --tier=unit --full
 *   node scripts/ci/dotnet-test.mjs --tier=unit --full --build
 *   node scripts/ci/dotnet-test.mjs --tier=integration
 *   node scripts/ci/dotnet-test.mjs --tier=unit --project "Comuki.Host.*"
 *
 * Zero third-party dependencies on purpose (same reasoning as
 * scripts/commit-lint.mjs): this has to run on a bare checkout with nothing
 * installed but node + the dotnet SDK.
 *
 * Result parsing: each `dotnet run` invocation is asked for a CTRF
 * (Common Test Report Format) JSON report via `--report-ctrf`. That is the
 * robust option over grepping the human-readable summary lines MTP prints
 * (`total: N`, `failed: N`, …) — those are localized (a non-`en` SDK/OS
 * prints "сбой:" instead of "failed:", which is exactly what silently
 * broke naive parsing before), while CTRF's JSON shape
 * (`results.summary.{tests,passed,failed,...}`, `results.tests[]`) is
 * locale-independent and stable across xUnit v3 versions.
 */
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const REPO_ROOT = path.resolve(path.dirname(__filename), '..', '..');

// ---------------------------------------------------------------------------
// Contract
// ---------------------------------------------------------------------------

export const TIERS = Object.freeze(['unit', 'integration']);

export const REPORT_SCHEMA_VERSION = 1;

/** Project directories that exist under `tests/integration/` but are shared
 * test infrastructure, not a runnable suite (no MTP entry point). */
const INTEGRATION_EXCLUDE = new Set(['Comuki.Host.Testing']);

const USAGE = `Usage: node scripts/ci/dotnet-test.mjs --tier=<unit|integration> [options]

Discovers and runs Comuki's xUnit v3 (MTP) test projects, then writes
report.json + report.md in the shape documented in
openspec/changes/add-agentic-test-contour/design.md ("Report format for
agents").

Options:
  --tier=<unit|integration>  Required. Which project set to discover.
  --full                     Unit tier only: also run tests/Comuki.Architecture.Tests
                              alongside tests/unit/*/. Ignored (accepted) for
                              --tier=integration, which always runs its full set
                              (tests/integration/*/ minus Comuki.Host.Testing).
  --project <glob>           Only run projects whose directory name matches this
                              glob ('*' wildcard, e.g. "Comuki.Host.*"). May be
                              combined with --full.
  --report-dir <dir>         Where to write report.json/report.md and per-project
                              artifacts (ctrf.json + run.log). Default:
                              artifacts/test-reports/<tier>.
  --build                    Run \`dotnet build comuki.slnx -c Debug\` once before
                              running any project. Without this flag the script
                              assumes the solution is already built (each project
                              runs with --no-build — build it yourself first).
  --help, -h                 Show this help and exit.
`;

// ---------------------------------------------------------------------------
// Argument parsing
// ---------------------------------------------------------------------------

/**
 * Parse argv into options. Accepts both `--flag=value` and `--flag value`.
 * @param {string[]} argv
 * @returns {{ ok: true, options: object } | { ok: false, error: string }}
 */
export function parseArgs(argv) {
  const options = {
    tier: undefined,
    full: false,
    project: undefined,
    reportDir: undefined,
    build: false,
    help: false,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    if (raw === '--help' || raw === '-h') {
      options.help = true;
      continue;
    }
    if (raw === '--full') {
      options.full = true;
      continue;
    }
    if (raw === '--build') {
      options.build = true;
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

    if (flag === '--tier') {
      const value = takeValue();
      if (value === undefined) {
        return { ok: false, error: '--tier needs a value (unit|integration)' };
      }
      options.tier = value;
      continue;
    }
    if (flag === '--project') {
      const value = takeValue();
      if (value === undefined) {
        return { ok: false, error: '--project needs a glob, e.g. --project "Comuki.Host.*"' };
      }
      options.project = value;
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

  if (options.help) {
    return { ok: true, options };
  }

  if (options.tier === undefined) {
    return { ok: false, error: 'missing required --tier=<unit|integration>' };
  }
  if (!TIERS.includes(options.tier)) {
    return { ok: false, error: `--tier must be one of ${TIERS.join('|')}, got "${options.tier}"` };
  }

  return { ok: true, options };
}

// ---------------------------------------------------------------------------
// Project discovery
// ---------------------------------------------------------------------------

/** Force forward slashes in a path used inside report.json/report.md, so
 * artifact paths are stable whether the script ran on Windows or Linux CI. */
function toPosix(p) {
  return p.split(path.sep).join('/');
}

/** Convert a `*`-wildcard glob into an anchored, case-sensitive RegExp. */
export function globToRegExp(glob) {
  const escaped = glob.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*');
  return new RegExp(`^${escaped}$`);
}

/** List immediate subdirectories of `root` as `{ name, dir }`, sorted by name. */
function listProjectDirs(root) {
  if (!existsSync(root)) {
    return [];
  }
  return readdirSync(root, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => ({ name: entry.name, dir: path.join(root, entry.name) }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

/**
 * Discover the runnable test projects for a tier.
 * @param {{ tier: 'unit'|'integration', full?: boolean, projectGlob?: string, repoRoot?: string }} opts
 * @returns {{ name: string, dir: string, csproj: string }[]}
 */
export function discoverProjects({ tier, full = false, projectGlob, repoRoot = REPO_ROOT }) {
  let candidates;
  if (tier === 'unit') {
    candidates = listProjectDirs(path.join(repoRoot, 'tests', 'unit'));
    if (full) {
      const archDir = path.join(repoRoot, 'tests', 'Comuki.Architecture.Tests');
      if (existsSync(archDir)) {
        candidates = [...candidates, { name: 'Comuki.Architecture.Tests', dir: archDir }];
      }
    }
  } else if (tier === 'integration') {
    candidates = listProjectDirs(path.join(repoRoot, 'tests', 'integration')).filter(
      (p) => !INTEGRATION_EXCLUDE.has(p.name),
    );
  } else {
    throw new Error(`unknown tier: ${tier}`);
  }

  if (projectGlob) {
    const pattern = globToRegExp(projectGlob);
    candidates = candidates.filter((p) => pattern.test(p.name));
  }

  return candidates
    .map((p) => ({ ...p, csproj: path.join(p.dir, `${p.name}.csproj`) }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

// ---------------------------------------------------------------------------
// CTRF result parsing
// ---------------------------------------------------------------------------

/**
 * Reduce a parsed CTRF report to the counts this script cares about.
 * @param {object} ctrf
 * @returns {{ total: number, passed: number, failed: number, skipped: number }}
 */
export function summarizeCtrf(ctrf) {
  const summary = ctrf?.results?.summary ?? {};
  const total = summary.tests ?? 0;
  const passed = summary.passed ?? 0;
  const failed = summary.failed ?? 0;
  const skipped = (summary.pending ?? 0) + (summary.skipped ?? 0) + (summary.other ?? 0);
  return { total, passed, failed, skipped };
}

/**
 * Build one report.json `failures[]` entry per failed test in a CTRF report.
 * `scenario`/`stage` reuse design.md's scenario-runner vocabulary: for a
 * dotnet test project the project IS the scenario (the unit of "did this
 * run cleanly") and each failing test is a stage within it.
 * @param {string} projectName
 * @param {object} ctrf
 * @param {string[]} artifactPaths
 */
export function failuresFromCtrf(projectName, ctrf, artifactPaths) {
  const tests = ctrf?.results?.tests ?? [];
  return tests
    .filter((t) => t.status === 'failed')
    .map((t) => ({
      scenario: projectName,
      stage: t.name,
      message: (t.message ?? 'test failed').split(/\r?\n/)[0],
      artifactPaths,
    }));
}

// ---------------------------------------------------------------------------
// Report envelope (design.md — "Report format for agents")
// ---------------------------------------------------------------------------

/**
 * @param {{ tier: string, startedAt: string, durationMs: number,
 *           summary: { total: number, passed: number, failed: number, skipped: number },
 *           failures: object[] }} data
 */
export function buildEnvelope({ tier, startedAt, durationMs, summary, failures }) {
  return {
    schemaVersion: REPORT_SCHEMA_VERSION,
    tier,
    // Fake/replay/live mode only means something for the scenario runner
    // (WS4+); a plain dotnet unit/integration run has no model in the loop.
    mode: null,
    startedAt,
    durationMs,
    summary: {
      total: summary.total,
      passed: summary.passed,
      failed: summary.failed,
      skipped: summary.skipped,
    },
    failures,
    // No model spend in T0/T1 — present for envelope consistency across tiers.
    cost: { usdMicros: 0, tokensIn: 0, tokensOut: 0 },
  };
}

/** One-line terminal verdict, same spirit as `test:affected`'s stdout line. */
export function formatVerdict(report) {
  const { total, passed, failed } = report.summary;
  if (failed === 0) {
    return `PASS ${passed}/${total}`;
  }
  return `FAIL ${failed}/${total} — see report.md`;
}

/**
 * Render the same envelope object as markdown: a summary line, a failures
 * table, and a "first failure" section with artifact paths inlined so an
 * agent can jump straight to the failing test's log without re-running
 * anything. Built from the JSON object, not recomputed, so the two never
 * drift (design.md's explicit requirement).
 */
export function renderMarkdown(report) {
  const lines = [];
  lines.push(`# Test report — ${report.tier}`, '');
  lines.push(`- Started: ${report.startedAt}`);
  lines.push(`- Duration: ${report.durationMs}ms`);
  lines.push(
    `- Summary: ${report.summary.total} total, ${report.summary.passed} passed, ` +
      `${report.summary.failed} failed, ${report.summary.skipped} skipped`,
  );
  lines.push('', `**${formatVerdict(report)}**`, '');

  if (report.failures.length === 0) {
    lines.push('No failures.');
    return `${lines.join('\n')}\n`;
  }

  lines.push('## Failures', '');
  lines.push('| Scenario | Stage | Message |');
  lines.push('| --- | --- | --- |');
  for (const f of report.failures) {
    const message = f.message.replace(/\|/g, '\\|');
    lines.push(`| ${f.scenario} | ${f.stage} | ${message} |`);
  }
  lines.push('');

  const first = report.failures[0];
  lines.push('## First failure', '');
  lines.push(`**${first.scenario}** — ${first.stage}`, '');
  lines.push('```');
  lines.push(first.message);
  lines.push('```', '');
  if (first.artifactPaths.length > 0) {
    lines.push('Artifacts:');
    for (const p of first.artifactPaths) {
      lines.push(`- ${p}`);
    }
    lines.push('');
  }

  return `${lines.join('\n')}\n`;
}

// ---------------------------------------------------------------------------
// Execution
// ---------------------------------------------------------------------------

function logGroupStart(name) {
  if (process.env.GITHUB_ACTIONS === 'true') {
    process.stdout.write(`::group::${name}\n`);
  } else {
    process.stdout.write(`--- ${name} ---\n`);
  }
}

function logGroupEnd() {
  if (process.env.GITHUB_ACTIONS === 'true') {
    process.stdout.write('::endgroup::\n');
  }
}

function warnAllSkipped(name) {
  if (process.env.GITHUB_ACTIONS === 'true') {
    process.stdout.write(`::warning title=${name}::every test in this suite is skipped\n`);
  } else {
    process.stdout.write(`WARN: ${name} — every test in this suite is skipped\n`);
  }
}

function buildSolution() {
  process.stdout.write('==> dotnet build comuki.slnx -c Debug\n');
  const result = spawnSync('dotnet', ['build', 'comuki.slnx', '-c', 'Debug'], {
    cwd: REPO_ROOT,
    stdio: 'inherit',
  });
  if (result.status !== 0) {
    process.stderr.write('dotnet build failed — aborting before running any tests\n');
    process.exit(typeof result.status === 'number' ? result.status : 1);
  }
}

/**
 * Run one project via `dotnet run --project <csproj> -c Debug --no-build`,
 * asking MTP for a CTRF report. Returns the per-project result used to
 * build the aggregate envelope.
 */
function runProject(project, reportDir) {
  const artifactsDir = path.join(reportDir, 'projects', project.name);
  mkdirSync(artifactsDir, { recursive: true });
  const ctrfFilename = 'ctrf.json';
  const ctrfPath = path.join(artifactsDir, ctrfFilename);
  const logPath = path.join(artifactsDir, 'run.log');
  const relLog = toPosix(path.relative(REPO_ROOT, logPath));
  const relCtrf = toPosix(path.relative(REPO_ROOT, ctrfPath));

  logGroupStart(project.name);

  const args = [
    'run',
    '--project',
    project.dir,
    '-c',
    'Debug',
    '--no-build',
    '--',
    '--report-ctrf',
    '--report-ctrf-filename',
    ctrfFilename,
    '--results-directory',
    artifactsDir,
  ];

  const startedAt = Date.now();
  const proc = spawnSync('dotnet', args, {
    cwd: REPO_ROOT,
    encoding: 'utf8',
    // English output keeps run.log grep-able for a human; result aggregation
    // below never depends on it (CTRF is parsed instead, on purpose).
    env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: 'en' },
  });
  const durationMs = Date.now() - startedAt;

  const combinedOutput = `${proc.stdout ?? ''}${proc.stderr ?? ''}`;
  writeFileSync(logPath, combinedOutput);
  process.stdout.write(combinedOutput);

  let result;
  if (!existsSync(ctrfPath)) {
    result = {
      name: project.name,
      counts: { total: 1, passed: 0, failed: 1, skipped: 0 },
      failures: [
        {
          scenario: project.name,
          stage: 'run',
          message: `dotnet run exited ${proc.status ?? proc.error?.message ?? 'unknown'} without producing a CTRF report`,
          artifactPaths: [relLog],
        },
      ],
    };
  } else {
    const ctrf = JSON.parse(readFileSync(ctrfPath, 'utf8'));
    const counts = summarizeCtrf(ctrf);
    if (counts.total === 0) {
      result = {
        name: project.name,
        counts: { total: 0, passed: 0, failed: 1, skipped: 0 },
        failures: [
          {
            scenario: project.name,
            stage: 'discovery',
            message: 'no tests discovered (broken test discovery, not an empty/skipped suite)',
            artifactPaths: [relLog, relCtrf],
          },
        ],
      };
    } else if (counts.failed > 0) {
      result = {
        name: project.name,
        counts,
        failures: failuresFromCtrf(project.name, ctrf, [relLog, relCtrf]),
      };
    } else {
      if (counts.skipped > 0 && counts.skipped === counts.total) {
        warnAllSkipped(project.name);
      }
      result = { name: project.name, counts, failures: [] };
    }
  }

  process.stdout.write(
    `[${project.name}] ${result.counts.passed}/${result.counts.total} passed` +
      `${result.failures.length > 0 ? ` — ${result.failures.length} FAILED` : ''} (${durationMs}ms)\n`,
  );
  logGroupEnd();

  return result;
}

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

  const projects = discoverProjects({
    tier: options.tier,
    full: options.full,
    projectGlob: options.project,
  });

  if (projects.length === 0) {
    process.stderr.write(
      `no project directories found for --tier=${options.tier}` +
        `${options.project ? ` matching --project "${options.project}"` : ''}\n`,
    );
    return 1;
  }

  const missing = projects.filter((p) => !existsSync(p.csproj));
  if (missing.length > 0) {
    for (const p of missing) {
      process.stderr.write(`ERROR: missing ${path.relative(REPO_ROOT, p.csproj)}\n`);
    }
    return 1;
  }

  const reportDir = path.isAbsolute(options.reportDir ?? '')
    ? options.reportDir
    : path.join(REPO_ROOT, options.reportDir ?? path.join('artifacts', 'test-reports', options.tier));
  mkdirSync(reportDir, { recursive: true });

  if (options.build) {
    buildSolution();
  }

  process.stdout.write(
    `dotnet-test: tier=${options.tier} full=${options.full} projects=${projects.length}\n`,
  );

  const startedAtIso = new Date().toISOString();
  const overallStart = Date.now();

  let total = 0;
  let passed = 0;
  let failed = 0;
  let skipped = 0;
  const failures = [];

  for (const project of projects) {
    const result = runProject(project, reportDir);
    total += result.counts.total;
    passed += result.counts.passed;
    failed += result.counts.failed;
    skipped += result.counts.skipped;
    failures.push(...result.failures);
  }

  const durationMs = Date.now() - overallStart;
  const report = buildEnvelope({
    tier: options.tier,
    startedAt: startedAtIso,
    durationMs,
    summary: { total, passed, failed, skipped },
    failures,
  });

  writeFileSync(path.join(reportDir, 'report.json'), `${JSON.stringify(report, null, 2)}\n`);
  writeFileSync(path.join(reportDir, 'report.md'), renderMarkdown(report));

  process.stdout.write(`\n${formatVerdict(report)}\n`);
  process.stdout.write(
    `report: ${toPosix(path.relative(REPO_ROOT, path.join(reportDir, 'report.json')))} / ` +
      `${toPosix(path.relative(REPO_ROOT, path.join(reportDir, 'report.md')))}\n`,
  );

  return failed > 0 ? 1 : 0;
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  process.exitCode = main(process.argv.slice(2));
}
