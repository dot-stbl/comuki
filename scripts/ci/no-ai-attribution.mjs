#!/usr/bin/env node
/**
 * scripts/ci/no-ai-attribution.mjs — server-side, non-bypassable layer of the
 * no-AI-attribution rule that lives in .agents/rules/process/no-ai-attribution.md.
 *
 * The local commit-msg hook (scripts/hooks/commit-msg → scripts/commit-lint.mjs)
 * strips AI authorship bylines from commit *messages* when the user runs
 * `git commit`. That hook is honour-system: `git commit --no-verify` skips it,
 * pushed history is past it, and the bot identity in the commit's
 * author/committer fields is not its concern. This script is what CI runs
 * over a range after the fact and fails the build on — a layer an author
 * cannot bypass without rewriting history.
 *
 *   node scripts/ci/no-ai-attribution.mjs --range <base>..<head>
 *   node scripts/ci/no-ai-attribution.mjs --range origin/master..HEAD --text-file pr.md
 *   node scripts/ci/no-ai-attribution.mjs --report-dir artifacts/no-ai-attribution
 *
 * One source of truth for vendor patterns: this file imports
 * `AI_VENDORS`, `AI_EMAIL_DOMAINS`, `alternation`, and `stripAttribution`
 * straight from scripts/commit-lint.mjs (which now exports them, alongside
 * the existing `stripAttribution`). Anything new added there is picked up
 * here automatically — no second list to drift.
 *
 * Comuki's own product attribution is **explicitly allowlisted**, not merely
 * absent from the vendor list — see `isComukiIdentity` for the bot-author
 * case and `isComukiTrailerLine` for the provenance-trailer case. The
 * exemption survives the vendor list growing later: the carve-out is a
 * deliberate decision a reviewer can see, not a coincidental string miss.
 *
 * The one check this script adds on top of the commit-msg hook is **commit
 * identity**: a co-author trailer the hook can strip is invisible to `git
 * log --format=%an:%ae`, but a `Co-Authored-By:` who is actually the commit's
 * author/committer leaves a vendor token in the name or domain of the commit
 * itself. Same vendor patterns, applied to a different field, in the same
 * shape (reason strings an agent can read).
 *
 * Report shape: `report.json` + `report.md` via the same `buildEnvelope` /
 * `formatVerdict` / `renderMarkdown` trio scripts/ci/dotnet-test.mjs uses (and
 * scripts/ci/test-affected.mjs reuses), documented in
 * openspec/changes/add-agentic-test-contour/design.md ("Report format for
 * agents"). One tier name (`no-ai-attribution`) keeps the envelope shape but
 * lets a CI dashboard filter for it specifically.
 *
 * Zero third-party dependencies, same reasoning as commit-lint.mjs and
 * dotnet-test.mjs: must run on a bare checkout with nothing installed but
 * node + the `git` CLI. No regex compilations of the vendor list at call
 * sites — built once from the imported exports.
 */
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import {
  AI_EMAIL_DOMAINS,
  AI_VENDORS,
  alternation,
  stripAttribution,
} from '../commit-lint.mjs';
import { buildEnvelope, formatVerdict, renderMarkdown } from './dotnet-test.mjs';

const __filename = fileURLToPath(import.meta.url);
const REPO_ROOT = path.resolve(path.dirname(__filename), '..', '..');

// ---------------------------------------------------------------------------
// Contract
// ---------------------------------------------------------------------------

/**
 * Record-level field separator. \u001f is the ASCII "Unit Separator" control
 * character — it is not a legal byte in commit subject or body text, so
 * splitting the git-log format on it is safe regardless of content.
 *
 * Exported because the test file (scripts/ci/no-ai-attribution.test.mjs)
 * builds a synthetic raw record by joining fields with this separator.
 */
export const FIELD_SEPARATOR = '\u001f';

/**
 * Record-level separator. \u001e is the ASCII "Record Separator" — marks the
 * boundary between one git-log commit record and the next.
 *
 * Same export reason as {@link FIELD_SEPARATOR}.
 */
export const RECORD_SEPARATOR = '\u001e';

const VENDOR_NAME_PATTERN = new RegExp(`\\b${alternation(AI_VENDORS)}`, 'i');
const VENDOR_DOMAIN_PATTERN = new RegExp(alternation(AI_EMAIL_DOMAINS), 'i');

const DEFAULT_REPORT_DIR = path.join('artifacts', 'test-reports', 'no-ai-attribution');

const USAGE = `Usage: node scripts/ci/no-ai-attribution.mjs [options]

Fails (exit 1) if any commit in a range — or the contents of a text file —
carries an AI authorship byline. Server-side counterpart to the local
commit-msg hook (scripts/hooks/commit-msg → scripts/commit-lint.mjs) that
authors can bypass with \`git commit --no-verify\`; this gate has no bypass
short of rewriting history. Report shape (report.json + report.md) is the
shared envelope scripts/ci/dotnet-test.mjs defines.

Options:
  --range <A..B>           Range of git revisions to scan (e.g. origin/master..HEAD).
                            Required unless --help/-h is given. The range is passed
                            verbatim to \`git log\` — anything \`git log\` accepts works.
  --text-file <path>       Optional path to a PR/MR description (or any text body) to
                            scan in addition to the commit range. The original is
                            copied next to report.json for traceability.
  --report-dir <dir>       Where to write report.json, report.md, and any
                            --text-file copy. Default: artifacts/test-reports/no-ai-attribution.
                            Absolute paths are honoured as-is; relative ones are joined
                            under the repo root.
  --help, -h                Show this help and exit.

Exit codes: 0 = clean (or no commits/--text-file to check), 1 = at least one
attribution hit OR git invocation failed (a bad range must surface loudly).
`;

// ---------------------------------------------------------------------------
// Argument parsing
// ---------------------------------------------------------------------------

/**
 * Parse argv into options. Accepts both `--flag=value` and `--flag value`.
 * Mirrors the loop structure in scripts/ci/dotnet-test.mjs and
 * scripts/ci/test-affected.mjs — copy because the same "next token is the
 * value unless it starts with `--`" rule is the project's house style.
 *
 * `--range` is the one required value flag; missing it returns `ok:false`
 * (unless `--help` was given — help wins so `--help --range=foo` still
 * prints usage rather than reporting a missing required flag).
 *
 * @param {string[]} argv
 * @returns {{ ok: true, options: object } | { ok: false, error: string }}
 */
export function parseArgs(argv) {
  const options = {
    range: undefined,
    textFile: undefined,
    reportDir: undefined,
    help: false,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    if (raw === '--help' || raw === '-h') {
      options.help = true;
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

    if (flag === '--range') {
      const value = takeValue();
      if (value === undefined) {
        return { ok: false, error: '--range needs a revision range, e.g. --range origin/master..HEAD' };
      }
      options.range = value;
      continue;
    }
    if (flag === '--text-file') {
      const value = takeValue();
      if (value === undefined) {
        return { ok: false, error: '--text-file needs a path' };
      }
      options.textFile = value;
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

  if (options.range === undefined) {
    return { ok: false, error: 'missing required --range=<base>..<head>' };
  }

  return { ok: true, options };
}

// ---------------------------------------------------------------------------
// Pure checks (no IO) — these compose the test fixtures
// ---------------------------------------------------------------------------

/**
 * Comuki's own product attribution is desired, never flagged — a commit
 * made by a Comuki worker carries a bot author identity (e.g. a GitHub
 * App "comuki[bot]") and trailers that record which Comuki run produced
 * it and who asked for it, not a third-party model byline. The exemption
 * below is explicit rather than relying on "comuki" happening not to
 * overlap AI_VENDORS today — a reviewer can see the carve-out is a
 * deliberate decision, and it survives the vendor list growing later.
 *
 * `generated-by:` is exempt only when its *value* names Comuki — the key
 * alone is not Comuki-namespaced (a hypothetical `Generated-by: <some
 * other vendor>` trailer must still be caught). `comuki-run:` /
 * `comuki-mission:` are exempt unconditionally — the key itself is
 * Comuki-namespaced. `requested-by:` records the human who asked for the
 * run (Comuki's own trailer convention, not a vendor claim) — exempt
 * unconditionally.
 */
export const COMUKI_NAMESPACED_TRAILER_KEYS = Object.freeze(['comuki-run', 'comuki-mission', 'requested-by']);

const COMUKI_NAME_PATTERN = /\bcomuki\b/i;

/**
 * True when a commit author/committer *name* is Comuki's own bot identity
 * (e.g. "Comuki", "Comuki Bot", the GitHub App form "comuki[bot]").
 * @param {string | null | undefined} name
 * @returns {boolean}
 */
export function isComukiIdentity(name) {
  return COMUKI_NAME_PATTERN.test((name ?? '').trim());
}

/**
 * True when a stripped message line is one of Comuki's own provenance
 * trailers — never a third-party vendor byline. See the doc comment above
 * {@link COMUKI_NAMESPACED_TRAILER_KEYS} for the `generated-by:` value-check
 * nuance.
 * @param {string} line
 * @returns {boolean}
 */
export function isComukiTrailerLine(line) {
  const trimmed = (line ?? '').trim();
  const colon = trimmed.indexOf(':');
  if (colon === -1) {
    return false;
  }
  const key = trimmed.slice(0, colon).trim().toLowerCase();
  const value = trimmed.slice(colon + 1).trim();
  if (COMUKI_NAMESPACED_TRAILER_KEYS.includes(key)) {
    return true;
  }
  if (key === 'generated-by') {
    return COMUKI_NAME_PATTERN.test(value);
  }
  return false;
}

/**
 * Check one name + email pair against the shared vendor patterns. Push a
 * short, human-readable reason per hit so a `{ reasons }` string list reads
 * naturally in report.md's failures table.
 *
 * `null`/`undefined`/empty inputs are tolerated so callers can pass partial
 * git metadata without pre-validating. A genuine "no info" identity is the
 * same as a clean one for our purposes — both yield an empty reasons list.
 *
 * @param {string | null | undefined} name
 * @param {string | null | undefined} email
 * @returns {string[]} human-readable reasons; empty means clean.
 */
export function checkVendorIdentity(name, email) {
  const reasons = [];
  const trimmedName = (name ?? '').trim();
  const trimmedEmail = (email ?? '').trim();
  // Comuki's own bot identity short-circuits both name and email vendor
  // checks — see isComukiIdentity's doc comment.
  if (isComukiIdentity(trimmedName)) {
    return reasons;
  }
  if (trimmedName && VENDOR_NAME_PATTERN.test(trimmedName)) {
    reasons.push(`vendor token in name "${trimmedName}"`);
  }
  if (trimmedEmail && VENDOR_DOMAIN_PATTERN.test(trimmedEmail)) {
    reasons.push(`vendor domain in email "${trimmedEmail}"`);
  }
  return reasons;
}

/**
 * Parse one raw record produced by `collectCommitRecords`'s `--format=<...>`
 * invocation back into the structured fields this script reasons about.
 *
 * The format string interleaves six fields with {@link FIELD_SEPARATOR} then
 * ends each record with {@link RECORD_SEPARATOR}; the first five fields are
 * short scalars (hash, four identity strings) so the destructuring is safe
 * to split by literal index. The body is everything after the fifth
 * separator — `bodyParts.join(FIELD_SEPARATOR)` rebuilds a body that itself
 * contained separator bytes, which git won't emit but a hand-written raw
 * record (test fixture) might.
 *
 * The leading `\r?\n` trim handles a single trailing newline git inserts
 * between adjacent records when the commit's `%B` payload has none — same
 * defensive trim scripts/commit-lint.mjs's runRangeMode already does for
 * its own `RECORD_SEPARATOR.split(...)`.
 *
 * @param {string} record
 * @returns {{ hash: string, authorName: string, authorEmail: string,
 *   committerName: string, committerEmail: string, body: string }}
 */
export function parseCommitRecord(record) {
  const cleaned = record.replace(/^\r?\n/, '');
  const parts = cleaned.split(FIELD_SEPARATOR);
  const [hash, authorName, authorEmail, committerName, committerEmail, ...bodyParts] = parts;
  return {
    hash: hash ?? '',
    authorName: authorName ?? '',
    authorEmail: authorEmail ?? '',
    committerName: committerName ?? '',
    committerEmail: committerEmail ?? '',
    body: bodyParts.join(FIELD_SEPARATOR),
  };
}

/**
 * Evaluate one parsed commit record. Combines:
 *
 *   1. `stripAttribution(body ?? '')` — runs the same shared rule the
 *      commit-msg hook uses (`%B` includes the subject line on line 1, so
 *      the result covers subject + body + trailers in one pass).
 *   2. `checkVendorIdentity` against the **author** name+email.
 *   3. `checkVendorIdentity` against the **committer** name+email —
 *      labelled separately so report.md makes clear which role the
 *      bot-identity hit landed in (a `Co-Authored-By:` trailer the hook
 *      could have stripped is invisible to `%an/%ae`; a vendor author the
 *      author field carries is not).
 *
 * Reason strings are prefixed (`message:`, `author …`, `committer …`) so
 * an agent reading `report.md`'s "First failure" block can tell at a
 * glance which gate fired — three different rules hit three different
 * surfaces of the same commit.
 *
 * @param {{ hash: string, authorName: string, authorEmail: string,
 *   committerName: string, committerEmail: string, body: string }} record
 * @returns {{ hash: string, reasons: string[] }}
 */
export function evaluateCommit({ hash, authorName, authorEmail, committerName, committerEmail, body }) {
  const reasons = [];
  const { removed } = stripAttribution(body ?? '');
  // Comuki's own provenance trailers are exempt from the message scan —
  // see isComukiTrailerLine's doc comment for the carve-out scope.
  for (const line of removed) {
    if (isComukiTrailerLine(line)) continue;
    reasons.push(`message: ${line}`);
  }
  for (const r of checkVendorIdentity(authorName, authorEmail)) {
    reasons.push(`author ${r}`);
  }
  for (const r of checkVendorIdentity(committerName, committerEmail)) {
    reasons.push(`committer ${r}`);
  }
  return { hash, reasons };
}

/**
 * Evaluate one free-form text body (PR/MR description, the `--text-file`
 * input). Identical to evaluateCommit's message scan but with no author
 * identity to check — descriptions carry bylines, not commits, so the
 * `label` parameter is what reason strings get prefixed with (main() passes
 * `'description'` for the MR/PR case so report.md shows the right
 * surface).
 *
 * @param {string} text
 * @param {string} label surface name used in reason prefixes
 * @returns {{ label: string, reasons: string[] }}
 */
export function evaluateText(text, label) {
  const { removed } = stripAttribution(text ?? '');
  // Comuki's own provenance trailers are exempt from the message scan —
  // see isComukiTrailerLine's doc comment for the carve-out scope.
  return {
    label,
    reasons: removed
      .filter((line) => !isComukiTrailerLine(line))
      .map((line) => `${label}: ${line}`),
  };
}

// ---------------------------------------------------------------------------
// IO
// ---------------------------------------------------------------------------

/**
 * Force forward slashes in a path used inside report content, so artifact
 * paths are stable whether this ran on Windows or Linux CI. Same one-liner
 * scripts/ci/dotnet-test.mjs and scripts/ci/test-affected.mjs carry (their
 * helper is a file-local `toPosix`; we redefine here rather than export
 * across projects — the helper is too small to deserve a cross-project
 * import for one call site).
 */
function toPosix(p) {
  return p.split(path.sep).join('/');
}

/**
 * IO. Run `git log --format=<hash><FS>%an<FS>%ae<FS>%cn<FS>%ce<FS>%B><RS> <range>`,
 * split the output on {@link RECORD_SEPARATOR}, drop empty pieces, parse each
 * remaining chunk as one commit record.
 *
 * Throws on non-zero git exit (caller — main() — turns that into stderr +
 * exit 1; a bad range must fail loudly, not silently pass).
 *
 * `repoRoot` is parameterised so tests can point this at a temp git repo
 * if/when integration tests land; today only the pure parseCommitRecord
 * path is exercised.
 *
 * @param {string} range
 * @param {string} [repoRoot=REPO_ROOT]
 * @returns {Array<ReturnType<typeof parseCommitRecord>>}
 */
export function collectCommitRecords(range, repoRoot = REPO_ROOT) {
  const format = `%H${FIELD_SEPARATOR}%an${FIELD_SEPARATOR}%ae${FIELD_SEPARATOR}%cn${FIELD_SEPARATOR}%ce${FIELD_SEPARATOR}%B${RECORD_SEPARATOR}`;
  const result = spawnSync('git', ['log', `--format=${format}`, range], {
    cwd: repoRoot,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });
  if (result.status !== 0) {
    const stderr = (result.stderr ?? '').trim();
    throw new Error(`git log --format=… ${range} failed (exit ${result.status ?? 'unknown'}): ${stderr}`);
  }
  const raw = result.stdout ?? '';
  const out = [];
  for (const record of raw.split(RECORD_SEPARATOR)) {
    const cleaned = record.replace(/^\r?\n/, '');
    if (cleaned.trim().length === 0) {
      continue;
    }
    out.push(parseCommitRecord(record));
  }
  return out;
}

/**
 * First non-blank line of a commit message — used as the `stage` field in
 * the report's failures[]. Mirrors the dotnet-test convention: scenario =
 * the unit of "did this run cleanly" (a commit hash prefix), stage = the
 * human label within it (the subject).
 *
 * Pure — text in, string out. Defined here rather than imported from
 * test-affected.mjs because the helpers there are scoped per-feature and
 * not exported across the `scripts/ci/` boundary today.
 */
function firstNonEmptyLine(text) {
  const line = (text ?? '').split(/\r?\n/).find((candidate) => candidate.trim().length > 0);
  return line ? line.trim() : '';
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

  const reportDir = path.isAbsolute(options.reportDir ?? '')
    ? options.reportDir
    : path.join(REPO_ROOT, options.reportDir ?? DEFAULT_REPORT_DIR);
  mkdirSync(reportDir, { recursive: true });

  const startedAtIso = new Date().toISOString();
  const overallStart = Date.now();

  let total = 0;
  let passed = 0;
  let failed = 0;
  const failures = [];

  let records;
  try {
    records = collectCommitRecords(options.range);
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    return 1;
  }

  for (const record of records) {
    total += 1;
    const verdict = evaluateCommit(record);
    if (verdict.reasons.length === 0) {
      passed += 1;
      continue;
    }
    failed += 1;
    failures.push({
      scenario: record.hash.slice(0, 10),
      stage: firstNonEmptyLine(record.body) || '(empty)',
      message: verdict.reasons.join('; '),
      artifactPaths: [],
    });
  }

  if (options.textFile !== undefined) {
    const text = readFileSync(options.textFile, 'utf8');
    const copyPath = path.join(reportDir, 'description.txt');
    writeFileSync(copyPath, text);
    const relArtifact = toPosix(path.relative(REPO_ROOT, copyPath));
    total += 1;
    const verdict = evaluateText(text, 'description');
    if (verdict.reasons.length === 0) {
      passed += 1;
    } else {
      failed += 1;
      failures.push({
        scenario: 'description',
        stage: 'text-file',
        message: verdict.reasons.join('; '),
        artifactPaths: [relArtifact],
      });
    }
  }

  const durationMs = Date.now() - overallStart;
  const report = buildEnvelope({
    tier: 'no-ai-attribution',
    startedAt: startedAtIso,
    durationMs,
    summary: { total, passed, failed, skipped: 0 },
    failures,
  });

  writeFileSync(path.join(reportDir, 'report.json'), `${JSON.stringify(report, null, 2)}\n`);
  writeFileSync(path.join(reportDir, 'report.md'), renderMarkdown(report));

  process.stdout.write(`no-ai-attribution: range=${options.range} total=${total} failed=${failed}\n`);
  process.stdout.write(`${formatVerdict(report)}\n`);
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
