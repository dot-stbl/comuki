#!/usr/bin/env node
/**
 * commit-lint — single source of truth for the `[.stbl]` commit subject
 * format and for the no-AI-attribution rule.
 *
 * Pure, exported functions (`stripAttribution`, `lintSubject`, `lintMessage`)
 * plus a small CLI:
 *
 *   node scripts/commit-lint.mjs --file .git/COMMIT_EDITMSG  # commit-msg hook
 *   node scripts/commit-lint.mjs --range master..HEAD        # manual audit
 *   node scripts/commit-lint.mjs --stdin                     # ad-hoc check
 *
 * Zero dependencies on purpose: the hook has to work on a bare clone with
 * nothing installed but node.
 *
 * Rules:
 *   .agents/rules/process/commit-format.md
 *   .agents/rules/process/no-ai-attribution.md
 */
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

// ---------------------------------------------------------------------------
// Contract
// ---------------------------------------------------------------------------

/**
 * Conventional Commits types accepted by this repo.
 *
 * `merge` is ours, not Conventional: a hand-written merge commit with a real
 * description (`[.stbl] merge(readme): OSS landing page`). The `Merge branch …`
 * subject git writes itself never gets here — see {@link EXEMPT_SUBJECT}.
 */
export const COMMIT_TYPES = Object.freeze([
  'feat',
  'fix',
  'refactor',
  'docs',
  'test',
  'perf',
  'build',
  'ci',
  'chore',
  'style',
  'revert',
  'merge',
]);

export const SUBJECT_MAX_LENGTH = 100;

export const COMMIT_FORMAT_RULE = '.agents/rules/process/commit-format.md';
export const NO_ATTRIBUTION_RULE = '.agents/rules/process/no-ai-attribution.md';

/** `[.stbl] <type>(<scope>)!: <description>` — legacy form, still accepted. */
const LEGACY_SUBJECT_PATTERN =
  /^\[\.stbl\] (?<type>[A-Za-z][A-Za-z0-9]*)(?:\((?<scope>[^()]*)\))?(?<breaking>!)?: (?<description>.*)$/;

/** `[.stbl](feat/<area>): <description>` — current form. Area is a kebab-case path. */
const FEATURE_SUBJECT_PATTERN =
  /^\[\.stbl\]\((?<path>[a-z][a-z0-9._\/-]*)\)(?<breaking>!)?: (?<description>.*)$/;

/**
 * Prefixes this repo has used and moved on from, newest first. Named
 * explicitly rather than folded into "missing prefix": every commit in this
 * history carries one of them, so an author reaching for muscle memory is
 * told which one it was and what replaced it.
 */
const RETIRED_PREFIXES = ['[hybrid]', '[stbl]'];

/**
 * Feature areas the rule's table lets stand alone. Every other root names a
 * kind of change rather than a place in the tree — `feat` without an area
 * says only "this is a feature", which the type already said — so it needs
 * a sub-area. See the Top-level areas table in the commit-format rule.
 */
const STANDALONE_AREAS = new Set(['meta', 'docs']);

const SCOPE_PATTERN = /^[a-z0-9][a-z0-9._\/-]*$/;

/** Subjects git writes itself — never the author's to fix.
 * Also exempts hand-written merge commits in the legacy `merge(<scope>): …`
 * form — see `.agents/rules/process/commit-format.md` §"Типы / merge".
 * The current-form `merge` is fine via the type/scope linting below.
 */
const EXEMPT_SUBJECT = /^(?:Merge\b|merge\([^()]*\):|Revert\b|fixup!|squash!|amend!)/;

/** `git commit --verbose` appends the diff below this marker. */
const SCISSORS = /^#\s*-+\s*>8\s*-+/;

const COMMENT_LINE = /^\s*#/;

// ---------------------------------------------------------------------------
// Attribution patterns
// ---------------------------------------------------------------------------

/** Vendor tokens that turn an authorship line into a model byline. */
export const AI_VENDORS = Object.freeze([
  'claude',
  'anthropic',
  'chatgpt',
  'openai',
  'gpt-',
  'codex',
  'copilot',
  'cursor',
  'gemini',
  'opencode',
  'devin',
  'aider',
  'windsurf',
]);

/** Domains that give a co-author away even when the display name looks human. */
export const AI_EMAIL_DOMAINS = Object.freeze([
  'anthropic',
  'openai',
  'copilot',
  'cursor.com',
  'cursor.sh',
]);

export function escapeRegExp(literal) {
  return literal.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export function alternation(tokens) {
  return `(?:${tokens.map(escapeRegExp).join('|')})`;
}

const VENDOR = alternation(AI_VENDORS);
const EMAIL_DOMAIN = alternation(AI_EMAIL_DOMAINS);

/**
 * Whole lines to delete. Tested against the *trimmed* line, case-insensitive.
 * Each one is a trailer shape, not prose — see `.agents/rules/process/no-ai-attribution.md`.
 */
const WHOLE_LINE_PATTERNS = Object.freeze([
  // Co-Authored-By: Claude <...>  /  Co-authored-by: Cursor Agent <...>
  new RegExp(`^co-authored-by:.*?\\b${VENDOR}`, 'i'),
  // Co-Authored-By: Someone Human <bot@anthropic.com>
  new RegExp(`^co-authored-by:[^<]*<[^>]*${EMAIL_DOMAIN}[^>]*>`, 'i'),
  // 🤖 Generated with [Claude Code](https://claude.com/claude-code)
  /^(?:🤖\s*)?generated with\b/i,
  // Co-authored by Claude / Written with Codex / Built by Cursor / Made with Gemini
  new RegExp(
    `^(?:co[-\\s])?(?:authored|written|designed|created|built|made)[-\\s](?:with|by)\\b[:\\s]*(?:the\\s+)?\\[?${VENDOR}`,
    'i',
  ),
  // Assisted-By: … / AI-generated / AI-assisted / AI-authored
  /^assisted-by:/i,
  /^ai[-\s]?(?:generated|assisted|authored)\b/i,
]);

/**
 * Fragments to excise from a line that also carries real text.
 * Global — a line may carry more than one.
 */
const INLINE_PATTERNS = Object.freeze([
  // 🤖 Generated with [Claude Code](https://claude.com/claude-code) and variants
  new RegExp(
    `\\s*(?:🤖\\s*)?generated with\\s+(?:\\[[^\\]\\n]*\\]\\([^)\\n]*\\)|${VENDOR}(?:\\s+code)?)`,
    'gi',
  ),
  // noreply@anthropic.com, bare or inside angle brackets.
  // Leading whitespace is eaten, trailing is not — excising from the middle of
  // a sentence must not glue the surrounding words together.
  /\s*<?noreply@anthropic\.com>?/gi,
]);

// ---------------------------------------------------------------------------
// stripAttribution
// ---------------------------------------------------------------------------

/**
 * Classify one message line.
 * @returns {{kind: 'keep'} | {kind: 'drop', removed: string} | {kind: 'edit', line: string, removed: string[]}}
 */
function classifyLine(rawLine) {
  const trimmed = rawLine.trim();
  if (trimmed.length === 0) {
    return { kind: 'keep' };
  }

  for (const pattern of WHOLE_LINE_PATTERNS) {
    if (pattern.test(trimmed)) {
      return { kind: 'drop', removed: trimmed };
    }
  }

  const fragments = [];
  let next = rawLine;
  for (const pattern of INLINE_PATTERNS) {
    pattern.lastIndex = 0;
    next = next.replace(pattern, (match) => {
      fragments.push(match.trim());
      return '';
    });
  }
  if (fragments.length === 0) {
    return { kind: 'keep' };
  }

  const cleaned = next.replace(/\s+$/, '');
  if (cleaned.trim().length === 0) {
    return { kind: 'drop', removed: trimmed };
  }
  return { kind: 'edit', line: cleaned, removed: fragments };
}

/**
 * Remove AI authorship bylines from a commit message.
 *
 * `#` comment lines and everything below a `--verbose` scissors marker are
 * left verbatim — the diff git shows there is not the author's text.
 * Idempotent: `strip(strip(x)) === strip(x)`.
 *
 * @param {string} message
 * @returns {{text: string, removed: string[]}}
 */
export function stripAttribution(message) {
  const text = typeof message === 'string' ? message : String(message ?? '');
  const eol = text.includes('\r\n') ? '\r\n' : '\n';
  const hadTrailingNewline = /\r?\n$/.test(text);

  const lines = text.split(/\r?\n/);
  if (hadTrailingNewline) {
    lines.pop();
  }

  const scissors = lines.findIndex((line) => SCISSORS.test(line));
  const end = scissors === -1 ? lines.length : scissors;
  const tail = lines.slice(end);

  const removed = [];
  const body = [];
  for (let index = 0; index < end; index += 1) {
    const line = lines[index];
    if (COMMENT_LINE.test(line)) {
      body.push(line);
      continue;
    }
    const verdict = classifyLine(line);
    if (verdict.kind === 'keep') {
      body.push(line);
    } else if (verdict.kind === 'edit') {
      body.push(verdict.line);
      removed.push(...verdict.removed);
    } else {
      removed.push(verdict.removed);
    }
  }

  // The removed trailer block leaves a blank run behind — collapse it. The run
  // is not necessarily at the very end of the file: git appends its `#`
  // template (and, with --verbose, the diff) below the author's text.
  if (removed.length > 0) {
    let last = body.length - 1;
    while (last >= 0 && (body[last].trim().length === 0 || COMMENT_LINE.test(body[last]))) {
      last -= 1;
    }
    let cut = last + 1;
    while (cut < body.length && body[cut].trim().length === 0) {
      cut += 1;
    }
    const followedByComments = cut < body.length || tail.length > 0;
    const separator = followedByComments && last >= 0 ? [''] : [];
    body.splice(last + 1, cut - (last + 1), ...separator);
  }

  const joined = [...body, ...tail].join(eol);
  return { text: hadTrailingNewline ? `${joined}${eol}` : joined, removed };
}

// ---------------------------------------------------------------------------
// Subject lint
// ---------------------------------------------------------------------------

/** First line that is neither blank, nor a `#` comment, nor below the scissors. */
export function extractSubject(message) {
  const text = typeof message === 'string' ? message : String(message ?? '');
  for (const line of text.split(/\r?\n/)) {
    if (SCISSORS.test(line)) {
      break;
    }
    if (COMMENT_LINE.test(line) || line.trim().length === 0) {
      continue;
    }
    return line.replace(/\s+$/, '');
  }
  return '';
}

function diagnoseShape(line) {
  const retired = RETIRED_PREFIXES.find((prefix) => line.toLowerCase().startsWith(prefix));
  if (retired) {
    return [`prefix \`${retired}\` is retired — this repo uses \`[.stbl]\``];
  }
  if (!line.startsWith('[.stbl]')) {
    if (!/^\([^()]+\): /.test(line) && !/^[A-Za-z]+(?:\([^()]*\))?!?: /.test(line)) {
      return ['missing `[.stbl]` prefix'];
    }
    // Conventional but unprefixed — lint the rest too, so the author sees
    // every problem at once instead of one per `--amend` round trip.
    // The length check is dropped: the prefix we just added is not theirs.
    const synthetic = line.startsWith('(') ? `[.stbl]${line}` : `[.stbl] ${line}`;
    const rest = lintSubject(synthetic).filter((problem) => !/^subject is \d+ char/.test(problem));
    return rest.length === 0
      ? [`missing \`[.stbl]\` prefix — write \`[.stbl]${line.startsWith('(') ? '' : ' '}${line}\``]
      : ['missing `[.stbl]` prefix', ...rest];
  }
  if (!line.startsWith('[.stbl](') && !line.startsWith('[.stbl] ')) {
    return ['`[.stbl]` must be followed by either ` ` + `<type>` or `(` + `<feat-area>`'];
  }

  const rest = line.slice(line.startsWith('[.stbl](') ? '[.stbl]'.length : '[.stbl] '.length);
  const colon = rest.indexOf(':');
  if (colon === -1) {
    return ['missing `:` between the type/area and the description'];
  }
  if (rest[colon + 1] !== ' ') {
    return ['`:` must be followed by a single space'];
  }
  const head = rest.slice(0, colon);
  if (head.length === 0) {
    return ['type or feature path is missing'];
  }
  if (line.startsWith('[.stbl] ')) {
    return [`cannot read \`${head}\` as \`<type>(<scope>)!\` — allowed types: ${COMMIT_TYPES.join(', ')}`];
  }
  return [`cannot read \`${head}\` as a feature path — use lowercase kebab-case with optional \`/\` (e.g. \`feat/dashboard\`, \`fe/mocks\`, \`meta\`)`];
}

/**
 * Lint a single subject line.
 * @param {string} subject
 * @returns {string[]} human-readable problems; empty means the subject is fine
 */
export function lintSubject(subject) {
  const line = (typeof subject === 'string' ? subject : String(subject ?? '')).replace(/\s+$/, '');
  if (line.length === 0) {
    return ['the commit message is empty'];
  }
  if (EXEMPT_SUBJECT.test(line)) {
    return [];
  }

  const problems = [];
  if (line.length > SUBJECT_MAX_LENGTH) {
    problems.push(`subject is ${line.length} characters — the limit is ${SUBJECT_MAX_LENGTH}`);
  }

  const legacyMatch = LEGACY_SUBJECT_PATTERN.exec(line);
  const featureMatch = legacyMatch === null ? FEATURE_SUBJECT_PATTERN.exec(line) : null;

  if (legacyMatch === null && featureMatch === null) {
    problems.push(...diagnoseShape(line));
    return problems;
  }

  let type, scope, path, description;
  if (legacyMatch !== null) {
    ({ type, scope, description } = legacyMatch.groups);
    if (!COMMIT_TYPES.includes(type)) {
      const lowered = type.toLowerCase();
      problems.push(
        COMMIT_TYPES.includes(lowered)
          ? `type \`${type}\` must be lowercase — write \`${lowered}\``
          : `unknown type \`${type}\` — allowed: ${COMMIT_TYPES.join(', ')}`,
      );
    }

    if (scope !== undefined) {
      if (scope.length === 0) {
        problems.push('empty scope `()` — name the area or drop the parentheses');
      } else if (!SCOPE_PATTERN.test(scope)) {
        problems.push(`scope \`${scope}\` must match \`[a-z0-9][a-z0-9._/-]*\``);
      }
    }
  } else {
    ({ path, description } = featureMatch.groups);
    if (!SCOPE_PATTERN.test(path)) {
      problems.push(`feature path \`${path}\` must match \`[a-z0-9][a-z0-9._/-]*\``);
    } else if (!path.includes('/') && !STANDALONE_AREAS.has(path)) {
      problems.push(
        `feature path \`${path}\` names a root with no area — write \`${path}/<area>\`` +
          ` (only ${[...STANDALONE_AREAS].map((area) => `\`${area}\``).join(' and ')} stand alone)`
      );
    }
  }

  if (description.trim().length === 0) {
    problems.push('description is empty');
  } else {
    if (/^\s/.test(description)) {
      problems.push('exactly one space between `:` and the description');
    }
    // No case check on the description on purpose: `commit-format.md` asks for
    // lowercase type/scope and an imperative description, not a lowercase one.
    // Descriptions legitimately open with an identifier or an acronym —
    // `SubjectScopeMiddleware wraps …`, `CVE-2026-49451 bump`, `OSS artifacts`.
    if (description.endsWith('.')) {
      problems.push('description must not end with `.`');
    }
  }

  return problems;
}

/**
 * Lint a whole commit message: strip attribution, then lint the subject of
 * what is left.
 * @param {string} message
 * @returns {{problems: string[], attribution: string[], subject: string}}
 */
export function lintMessage(message) {
  const { text, removed } = stripAttribution(message);
  const subject = extractSubject(text);
  return { problems: lintSubject(subject), attribution: removed, subject };
}

// ---------------------------------------------------------------------------
// Reporting
// ---------------------------------------------------------------------------

const CHEATSHEET = Object.freeze([
  '  Format:  [.stbl](feat/<area>): <description>    (current)',
  '           [.stbl] <type>(<scope>): <description>   (legacy, accepted)',
  '  Types:   feat fix refactor docs test perf build ci chore style revert merge',
  '  Path:    required after [.stbl](...) — lowercase kebab-case, may nest',
  '           (feat/dashboard), (fe/mocks), (meta), (host/mtls), (tests/architecture)',
  '  Scope:   legacy form only — lowercase — (orchestration), (dev-sdk), (rules)',
  `  Subject: imperative, no trailing dot, <= ${SUBJECT_MAX_LENGTH} chars (aim for 72)`,
  '',
  '  Example: [.stbl](feat/dashboard): wire cost page breakdowns and forecast',
  '  Example: [.stbl] fix(database): correct cascade delete on runs table',
  '',
  `  Rule:    ${COMMIT_FORMAT_RULE}`,
  '  Bypass:  git commit --no-verify',
]);

export function formatSubjectReport(subject, problems, { heading = 'commit message rejected' } = {}) {
  const lines = ['', `❌ ${heading}`, '', `  ${subject.length === 0 ? '(empty)' : subject}`, ''];
  for (const problem of problems) {
    lines.push(`  • ${problem}`);
  }
  lines.push('', ...CHEATSHEET, '');
  return lines.join('\n');
}

export function formatAttributionReport(removed, { fixed }) {
  const label = removed.length === 1 ? 'line' : 'lines';
  const lines = [
    '',
    fixed
      ? `⚠️  stripped ${removed.length} AI attribution ${label} from the commit message:`
      : `❌ ${removed.length} AI attribution ${label} in the commit message:`,
  ];
  for (const entry of removed) {
    lines.push(`     ${entry}`);
  }
  lines.push(`   Rule: ${NO_ATTRIBUTION_RULE}`, '');
  return lines.join('\n');
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

const USAGE = `commit-lint — [.stbl] commit format + no-AI-attribution gate

Usage:
  node scripts/commit-lint.mjs --file <path>     commit-msg mode: strip attribution in place, lint the subject
  node scripts/commit-lint.mjs --range <A..B>    audit existing commits (attribution is an error here)
  node scripts/commit-lint.mjs --stdin           lint a message piped in

${CHEATSHEET.join('\n')}
`;

function runFileMode(path) {
  const original = readFileSync(path, 'utf8');
  const { text, removed } = stripAttribution(original);

  if (removed.length > 0) {
    writeFileSync(path, text, 'utf8');
    process.stderr.write(formatAttributionReport(removed, { fixed: true }));
  }

  const subject = extractSubject(text);
  const problems = lintSubject(subject);
  if (problems.length > 0) {
    process.stderr.write(formatSubjectReport(subject, problems));
    return 1;
  }
  return 0;
}

function runStdinMode() {
  const message = readStdin();
  const { problems, attribution, subject } = lintMessage(message);

  if (attribution.length > 0) {
    process.stderr.write(formatAttributionReport(attribution, { fixed: false }));
  }
  if (problems.length > 0) {
    process.stderr.write(formatSubjectReport(subject, problems));
  }
  return attribution.length > 0 || problems.length > 0 ? 1 : 0;
}

const RECORD_SEPARATOR = '\u001e';
const FIELD_SEPARATOR = '\u001f';

function runRangeMode(range) {
  const raw = execFileSync('git', ['log', `--format=%H${FIELD_SEPARATOR}%B${RECORD_SEPARATOR}`, range], {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });

  let failures = 0;
  let checked = 0;
  for (const record of raw.split(RECORD_SEPARATOR)) {
    const cleaned = record.replace(/^\r?\n/, '');
    if (cleaned.trim().length === 0) {
      continue;
    }
    const split = cleaned.indexOf(FIELD_SEPARATOR);
    const hash = cleaned.slice(0, split);
    const body = cleaned.slice(split + 1);
    checked += 1;

    const { problems, attribution, subject } = lintMessage(body);
    if (attribution.length === 0 && problems.length === 0) {
      continue;
    }

    failures += 1;
    process.stderr.write(`\n${hash.slice(0, 10)}  ${subject.length === 0 ? '(empty)' : subject}\n`);
    for (const entry of attribution) {
      process.stderr.write(`  • AI attribution: ${entry}\n`);
    }
    for (const problem of problems) {
      process.stderr.write(`  • ${problem}\n`);
    }
  }

  if (failures > 0) {
    process.stderr.write(`\n❌ ${failures} of ${checked} commit(s) in \`${range}\` violate the commit rules\n`);
    process.stderr.write(`   Format: ${COMMIT_FORMAT_RULE}\n`);
    process.stderr.write(`   Bylines: ${NO_ATTRIBUTION_RULE}\n\n`);
    return 1;
  }
  process.stdout.write(`✅ ${checked} commit(s) in \`${range}\` are clean\n`);
  return 0;
}

function readStdin() {
  try {
    return readFileSync(0, 'utf8');
  } catch {
    return '';
  }
}

export function main(argv) {
  if (argv.length === 0 || argv.includes('--help') || argv.includes('-h')) {
    process.stdout.write(USAGE);
    return argv.length === 0 ? 1 : 0;
  }

  const value = (flag) => {
    const index = argv.indexOf(flag);
    return index === -1 ? undefined : argv[index + 1];
  };

  if (argv.includes('--file')) {
    const path = value('--file');
    if (path === undefined) {
      process.stderr.write('--file needs a path\n');
      return 1;
    }
    return runFileMode(path);
  }

  if (argv.includes('--range')) {
    const range = value('--range');
    if (range === undefined) {
      process.stderr.write('--range needs a revision range, e.g. master..HEAD\n');
      return 1;
    }
    return runRangeMode(range);
  }

  if (argv.includes('--stdin')) {
    return runStdinMode();
  }

  process.stderr.write(USAGE);
  return 1;
}

const entry = process.argv[1];
if (entry !== undefined && import.meta.url === pathToFileURL(entry).href) {
  process.exitCode = main(process.argv.slice(2));
}
