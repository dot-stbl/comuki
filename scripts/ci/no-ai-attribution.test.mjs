/**
 * Tests for scripts/ci/no-ai-attribution.mjs — `node --test scripts/ci/no-ai-attribution.test.mjs`.
 * Zero dependencies: node:test + node:assert/strict only (same style as
 * scripts/commit-lint.test.mjs, scripts/ci/dotnet-test.test.mjs,
 * scripts/ci/test-affected.test.mjs).
 *
 * What this gate is for — seven scenarios the spec must cover, no more, no
 * fewer:
 *
 *   1. patterns   — every token in AI_VENDORS and every domain in
 *                   AI_EMAIL_DOMAINS lands a name/email hit, and a clean
 *                   identity (human name + hybrid.ai email) is silent.
 *   2. clean commit passes — synthetic parsed record, clean everything.
 *   3. dirty message fails — body carries a Co-Authored-By trailer.
 *   4. bot author fails — author name + email both vendor-shaped, committer
 *                          stays human.
 *   5. description scan     — PR-description byline flagged, clean
 *                             multi-paragraph description silent.
 *   6. Comuki allowlist     — Comuki's own bot identity and provenance
 *                             trailers are exempt, and the exemption does
 *                             NOT swallow a real vendor byline riding
 *                             alongside Comuki trailers in the same body.
 *   7. Generated-by trailer — the colon-keyed "generated-by:" shape isn't
 *                             recognised by commit-lint.mjs's
 *                             stripAttribution at all, so a non-Comuki
 *                             `Generated-by: <vendor>` is checked
 *                             independently (findVendorGeneratedByLines).
 *
 * Plus: parseCommitRecord round-trip and a parseArgs battery covering
 * defaults, --range value / =value forms, missing --range as a parse
 * error, --help short-circuit.
 *
 * Fixtures live in JavaScript (synthetic parsed record, raw-byte strings),
 * never against a real git repo or a real `git log` — same approach as
 * scripts/ci/test-affected.test.mjs, which also never spawns real git
 * plumbing.
 */
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { AI_EMAIL_DOMAINS, AI_VENDORS } from '../commit-lint.mjs';
import {
  FIELD_SEPARATOR,
  RECORD_SEPARATOR,
  checkVendorIdentity,
  evaluateCommit,
  evaluateText,
  findVendorGeneratedByLines,
  isComukiIdentity,
  isComukiTrailerLine,
  parseArgs,
  parseCommitRecord,
} from './no-ai-attribution.mjs';

// ---------------------------------------------------------------------------
// 1. patterns — every vendor token + every email domain lands a hit,
//               a clean identity is silent
// ---------------------------------------------------------------------------

describe('checkVendorIdentity — patterns', () => {
  it('every token in AI_VENDORS is caught when it appears in the name', () => {
    for (const token of AI_VENDORS) {
      const reasons = checkVendorIdentity(`${token} Bot`, undefined);
      assert.ok(
        reasons.length > 0,
        `expected checkVendorIdentity(\`${token} Bot\`, undefined) to flag, got []`,
      );
    }
  });

  it('every domain in AI_EMAIL_DOMAINS is caught when it appears in the email', () => {
    for (const domain of AI_EMAIL_DOMAINS) {
      const reasons = checkVendorIdentity(undefined, `bot@${domain}`);
      assert.ok(
        reasons.length > 0,
        `expected checkVendorIdentity(undefined, \`bot@${domain}\`) to flag, got []`,
      );
    }
  });

  it('passes a clean human identity unchanged', () => {
    assert.deepEqual(checkVendorIdentity('Jane Doe', 'jane@hybrid.ai'), []);
  });

  it('treats missing/empty name and email as no-identity (no spurious hits)', () => {
    assert.deepEqual(checkVendorIdentity(undefined, undefined), []);
    assert.deepEqual(checkVendorIdentity('', ''), []);
    assert.deepEqual(checkVendorIdentity('   ', '   '), []);
  });

  it('flags a vendor token in the name even when the email is human-shaped', () => {
    const reasons = checkVendorIdentity('Claude Code', 'jane@hybrid.ai');
    assert.equal(reasons.length, 1);
    assert.match(reasons[0], /vendor token in name/);
  });

  it('flags a vendor domain in the email even when the name is human', () => {
    const reasons = checkVendorIdentity('Jane Doe', 'jane@anthropic.com');
    assert.equal(reasons.length, 1);
    assert.match(reasons[0], /vendor domain in email/);
  });

  it('flags both name and email independently when both are vendor-shaped', () => {
    const reasons = checkVendorIdentity('Claude Code', 'bot@anthropic.com');
    assert.equal(reasons.length, 2);
  });
});

// ---------------------------------------------------------------------------
// 2. clean range passes — synthetic parsed record with clean everything
// ---------------------------------------------------------------------------

describe('evaluateCommit — clean synthetic record', () => {
  it('reports zero reasons for a clean author, clean committer, clean body', () => {
    const record = {
      hash: '0123456789abcdef0123456789abcdef01234567',
      authorName: 'Jane Doe',
      authorEmail: 'jane@hybrid.ai',
      committerName: 'Jane Doe',
      committerEmail: 'jane@hybrid.ai',
      body: '[.stbl](feat/x): do the thing\n\nSome plain body.\n',
    };
    const verdict = evaluateCommit(record);
    assert.deepEqual(verdict.reasons, []);
  });
});

// ---------------------------------------------------------------------------
// 3. dirty message fails — body carries a Co-Authored-By trailer
// ---------------------------------------------------------------------------

describe('evaluateCommit — dirty message', () => {
  it('flags a Co-Authored-By trailer in the body and prefixes the reason with "message:"', () => {
    const record = {
      hash: '0123456789abcdef0123456789abcdef01234567',
      authorName: 'Jane Doe',
      authorEmail: 'jane@hybrid.ai',
      committerName: 'Jane Doe',
      committerEmail: 'jane@hybrid.ai',
      body:
        '[.stbl](feat/x): do the thing\n\nReal change notes.\n\n' +
        'Co-Authored-By: Claude <noreply@anthropic.com>\n',
    };
    const verdict = evaluateCommit(record);
    assert.ok(verdict.reasons.length > 0, 'expected reasons > 0 for a co-author trailer');
    assert.ok(
      verdict.reasons.some((reason) => reason.startsWith('message:')),
      `expected at least one reason starting with "message:", got ${JSON.stringify(verdict.reasons)}`,
    );
  });

  it('catches the inline "noreply@anthropic.com" shape as well', () => {
    const record = {
      hash: '0123456789abcdef0123456789abcdef01234567',
      authorName: 'Jane Doe',
      authorEmail: 'jane@hybrid.ai',
      committerName: 'Jane Doe',
      committerEmail: 'jane@hybrid.ai',
      body:
        '[.stbl](feat/x): do the thing\n\nPing noreply@anthropic.com for details.\n',
    };
    const verdict = evaluateCommit(record);
    assert.ok(verdict.reasons.length > 0);
    assert.ok(verdict.reasons.some((reason) => reason.startsWith('message:')));
  });
});

// ---------------------------------------------------------------------------
// 4. bot author fails — vendor-shaped author, human committer
// ---------------------------------------------------------------------------

describe('evaluateCommit — bot author', () => {
  it('flags a vendor-shaped author and prefixes the reason with "author"', () => {
    const record = {
      hash: '0123456789abcdef0123456789abcdef01234567',
      authorName: 'Claude Code',
      authorEmail: 'noreply@anthropic.com',
      committerName: 'Jane Doe',
      committerEmail: 'jane@hybrid.ai',
      body: '[.stbl](feat/x): do the thing\n\nBody, clean, no bylines.\n',
    };
    const verdict = evaluateCommit(record);
    assert.ok(verdict.reasons.length > 0, 'expected reasons > 0 for vendor author');
    assert.ok(
      verdict.reasons.some((reason) => reason.startsWith('author')),
      `expected at least one reason starting with "author", got ${JSON.stringify(verdict.reasons)}`,
    );
  });

  it('flags a vendor-shaped committer and prefixes the reason with "committer"', () => {
    const record = {
      hash: '0123456789abcdef0123456789abcdef01234567',
      authorName: 'Jane Doe',
      authorEmail: 'jane@hybrid.ai',
      committerName: 'OpenAI Codex',
      committerEmail: 'codex@openai.com',
      body: '[.stbl](feat/x): do the thing\n\nBody, clean.\n',
    };
    const verdict = evaluateCommit(record);
    assert.ok(verdict.reasons.length > 0);
    assert.ok(
      verdict.reasons.some((reason) => reason.startsWith('committer')),
      `expected at least one reason starting with "committer", got ${JSON.stringify(verdict.reasons)}`,
    );
  });
});

// ---------------------------------------------------------------------------
// 5. description scan — PR-description byline flagged, clean description silent
// ---------------------------------------------------------------------------

describe('evaluateText — description scan', () => {
  it('flags the 🤖 Generated-with footer as a description byline', () => {
    const description = [
      '## Summary',
      '',
      'What this PR does.',
      '',
      '## Testing',
      '',
      'Test plan in narrative form.',
      '',
      '🤖 Generated with [Claude Code](https://claude.com/claude-code)',
      '',
    ].join('\n');
    const verdict = evaluateText(description, 'description');
    assert.ok(
      verdict.reasons.length > 0,
      `expected reasons > 0 for description byline, got ${JSON.stringify(verdict.reasons)}`,
    );
    assert.ok(verdict.reasons.every((reason) => reason.startsWith('description:')));
  });

  it('passes a multi-paragraph description with no byline', () => {
    const description = [
      '## Summary',
      '',
      'What this PR does, explained in prose across two paragraphs.',
      '',
      '## Testing',
      '',
      'How to verify the change.',
      '',
      '## Risks',
      '',
      'None expected; covered by existing tests.',
      '',
    ].join('\n');
    const verdict = evaluateText(description, 'description');
    assert.deepEqual(verdict.reasons, []);
  });

  it('uses the label parameter so callers can rename the surface without editing reasons', () => {
    const verdict = evaluateText(
      '🤖 Generated with [Claude Code](https://claude.com/claude-code)\n',
      'mr-description',
    );
    assert.equal(verdict.label, 'mr-description');
    assert.ok(verdict.reasons.every((reason) => reason.startsWith('mr-description:')));
  });
});

// ---------------------------------------------------------------------------
// 6. Comuki allowlist — Comuki's own bot identity and provenance trailers
//    are exempt, but the exemption must NOT swallow a real vendor byline
//    riding alongside Comuki trailers in the same commit
// ---------------------------------------------------------------------------

describe('Comuki allowlist', () => {
  it('isComukiIdentity accepts Comuki-name strings and rejects humans / empty / undefined', () => {
    assert.equal(isComukiIdentity('Comuki'), true);
    assert.equal(isComukiIdentity('comuki[bot]'), true);
    assert.equal(isComukiIdentity('Comuki Bot'), true);
    assert.equal(isComukiIdentity('Jane Doe'), false);
    assert.equal(isComukiIdentity('Claude Code'), false);
    assert.equal(isComukiIdentity(undefined), false);
    assert.equal(isComukiIdentity(''), false);
    assert.equal(isComukiIdentity('   '), false);
  });

  it('isComukiTrailerLine accepts Comuki-namespaced trailers and Generated-by-whose-value-names-Comuki, rejects everything else', () => {
    assert.equal(isComukiTrailerLine('Generated-by: Comuki v1.2.3'), true);
    // The precision case — key alone is not enough; the value must name Comuki.
    assert.equal(isComukiTrailerLine('Generated-by: Claude'), false);
    assert.equal(isComukiTrailerLine('Comuki-Run: abc123'), true);
    assert.equal(isComukiTrailerLine('Comuki-Mission: xyz789'), true);
    assert.equal(isComukiTrailerLine('Requested-by: Jane Doe'), true);
    assert.equal(
      isComukiTrailerLine('Co-Authored-By: Claude <noreply@anthropic.com>'),
      false,
    );
    assert.equal(isComukiTrailerLine('not a trailer at all'), false);
  });

  it('checkVendorIdentity short-circuits to [] for Comuki-name identities, even with vendor-shaped emails', () => {
    assert.deepEqual(checkVendorIdentity('Comuki', 'bot@hybrid.ai'), []);
    assert.deepEqual(
      checkVendorIdentity(
        'comuki[bot]',
        '41898282+comuki[bot]@users.noreply.github.com',
      ),
      [],
    );
  });

  it('evaluateCommit passes a synthetic Comuki-authored record whose body carries all four provenance trailers', () => {
    const record = {
      hash: '0123456789abcdef0123456789abcdef01234567',
      authorName: 'comuki[bot]',
      authorEmail: '41898282+comuki[bot]@users.noreply.github.com',
      committerName: 'comuki[bot]',
      committerEmail: '41898282+comuki[bot]@users.noreply.github.com',
      body:
        '[.stbl](feat/meta/ci): a change with Comuki provenance\n\n' +
        'Ordinary change-log prose describing what the patch does.\n\n' +
        'Generated-by: Comuki v1.0.0\n' +
        'Comuki-Run: run-42\n' +
        'Comuki-Mission: mission-7\n' +
        'Requested-by: Jane Doe\n',
    };
    const verdict = evaluateCommit(record);
    assert.deepEqual(verdict.reasons, []);
  });

  it('evaluateCommit does NOT swallow a real vendor byline riding alongside Comuki trailers — the Claude byline still fires', () => {
    const record = {
      hash: '0123456789abcdef0123456789abcdef01234567',
      authorName: 'Jane Doe',
      authorEmail: 'jane@hybrid.ai',
      committerName: 'Jane Doe',
      committerEmail: 'jane@hybrid.ai',
      body:
        '[.stbl](feat/x): do the thing\n\n' +
        'Body prose.\n\n' +
        'Comuki-Run: run-42\n' +
        'Co-Authored-By: Claude <noreply@anthropic.com>\n',
    };
    const verdict = evaluateCommit(record);
    assert.ok(
      verdict.reasons.length > 0,
      `expected reasons > 0 when a Claude byline rides with a Comuki trailer, got ${JSON.stringify(verdict.reasons)}`,
    );
    assert.ok(
      verdict.reasons.some((reason) => reason.includes('Claude')),
      `expected at least one reason mentioning Claude, got ${JSON.stringify(verdict.reasons)}`,
    );
    assert.ok(
      !verdict.reasons.some((reason) => reason.includes('Comuki-Run')),
      `expected no reason to mention the Comuki trailer (it should stay silent), got ${JSON.stringify(verdict.reasons)}`,
    );
  });

  it('evaluateText passes a description whose only byline is a Comuki-namespaced Generated-by trailer', () => {
    const verdict = evaluateText('Generated-by: Comuki v2.0.0\n', 'description');
    assert.deepEqual(verdict.reasons, []);
  });
});

// ---------------------------------------------------------------------------
// findVendorGeneratedByLines — the colon-keyed "generated-by:" trailer shape
// is NOT recognised by commit-lint.mjs's stripAttribution at all (it only
// knows the prose verbs and the literal "generated with" phrase), but this
// script introduced that exact shape as Comuki's own trailer convention —
// so a non-Comuki "Generated-by: <vendor>" must still be caught, checked
// independently of stripAttribution's `removed` set.
// ---------------------------------------------------------------------------

describe('findVendorGeneratedByLines', () => {
  it('catches a non-Comuki vendor named in a Generated-by trailer', () => {
    const hits = findVendorGeneratedByLines('subject\n\nGenerated-by: Claude Code\n');
    assert.deepEqual(hits, ['Generated-by: Claude Code']);
  });

  it('leaves the Comuki-namespaced form alone (isComukiTrailerLine already allows it)', () => {
    assert.deepEqual(findVendorGeneratedByLines('Generated-by: Comuki v1.0.0\n'), []);
  });

  it('leaves a Generated-by trailer with no vendor token alone', () => {
    assert.deepEqual(findVendorGeneratedByLines('Generated-by: a human, by hand\n'), []);
  });

  it('ignores lines that are not the generated-by shape at all', () => {
    assert.deepEqual(findVendorGeneratedByLines('Comuki-Run: run-42\nSome prose.\n'), []);
  });

  it('evaluateCommit catches a non-Comuki Generated-by trailer that stripAttribution alone would miss', () => {
    const record = {
      hash: '0123456789abcdef0123456789abcdef01234567',
      authorName: 'Jane Doe',
      authorEmail: 'jane@hybrid.ai',
      committerName: 'Jane Doe',
      committerEmail: 'jane@hybrid.ai',
      body: '[.stbl](feat/x): do the thing\n\nBody prose.\n\nGenerated-by: OpenAI Codex\n',
    };
    const verdict = evaluateCommit(record);
    assert.ok(
      verdict.reasons.some((reason) => reason.includes('Generated-by: OpenAI Codex')),
      `expected a reason for the Generated-by trailer, got ${JSON.stringify(verdict.reasons)}`,
    );
  });

  it('evaluateText catches a non-Comuki Generated-by trailer in a description', () => {
    const verdict = evaluateText('Generated-by: Claude\n', 'description');
    assert.ok(
      verdict.reasons.some((reason) => reason.includes('Generated-by: Claude')),
      `expected a reason for the Generated-by trailer, got ${JSON.stringify(verdict.reasons)}`,
    );
  });
});

// ---------------------------------------------------------------------------
// parseCommitRecord round-trip
// ---------------------------------------------------------------------------

describe('parseCommitRecord', () => {
  it('rebuilds every field from a raw record built with FIELD_SEPARATOR-joined values', () => {
    const hash = '0123456789abcdef0123456789abcdef01234567';
    const author = 'Jane Doe';
    const aemail = 'jane@hybrid.ai';
    const committer = 'Jane Doe';
    const cemail = 'jane@hybrid.ai';
    const body = '[.stbl](feat/x): do the thing\n\nBody text.\n';
    const raw = [hash, author, aemail, committer, cemail, body].join(FIELD_SEPARATOR);
    const parsed = parseCommitRecord(raw);
    assert.equal(parsed.hash, hash);
    assert.equal(parsed.authorName, author);
    assert.equal(parsed.authorEmail, aemail);
    assert.equal(parsed.committerName, committer);
    assert.equal(parsed.committerEmail, cemail);
    assert.equal(parsed.body, body);
  });

  it('preserves an embedded newline in the body — bodyParts.join rebuilds it', () => {
    const body = 'subject\n\nLine one\nLine two\nLine three\n';
    const raw = ['h', 'a', 'ae', 'c', 'ce', body].join(FIELD_SEPARATOR);
    const parsed = parseCommitRecord(raw);
    assert.equal(parsed.body, body);
  });

  it('preserves a body byte that equals FIELD_SEPARATOR (defensive — git would not emit it)', () => {
    const body = `before${FIELD_SEPARATOR}after\n`;
    const raw = ['h', 'a', 'ae', 'c', 'ce', body].join(FIELD_SEPARATOR);
    const parsed = parseCommitRecord(raw);
    assert.equal(parsed.body, body);
    assert.ok(parsed.body.includes(FIELD_SEPARATOR));
  });

  it('trims a single leading CR or LF that git inserts between adjacent records', () => {
    // Simulate a chunk post-split by the upstream collector (which has
    // already consumed RECORD_SEPARATOR), with a stray \n prefix from a
    // soft line break in the previous record's body bleeding into ours.
    const properRaw = `\n${'h'}${FIELD_SEPARATOR}An${FIELD_SEPARATOR}ae${FIELD_SEPARATOR}Cn${FIELD_SEPARATOR}ce${FIELD_SEPARATOR}B`;
    const parsed = parseCommitRecord(properRaw);
    assert.equal(parsed.hash, 'h');
    assert.equal(parsed.body, 'B');
    assert.ok(!parsed.hash.includes('\n'), 'leading newline should be trimmed by parseCommitRecord');
  });
});

// ---------------------------------------------------------------------------
// parseArgs
// ---------------------------------------------------------------------------

describe('parseArgs', () => {
  it('defaults everything to unset/false when called with no flags', () => {
    const { ok, error } = parseArgs([]);
    assert.equal(ok, false);
    assert.match(error, /missing required --range/);
  });

  it('accepts --range=value as a single-arg form', () => {
    const { ok, options } = parseArgs(['--range=origin/master..HEAD']);
    assert.ok(ok);
    assert.equal(options.range, 'origin/master..HEAD');
    assert.equal(options.textFile, undefined);
    assert.equal(options.reportDir, undefined);
    assert.equal(options.help, false);
  });

  it('accepts --range value as a two-arg form', () => {
    const { ok, options } = parseArgs(['--range', 'master..HEAD']);
    assert.ok(ok);
    assert.equal(options.range, 'master..HEAD');
  });

  it('accepts --text-file=value and --text-file value forms', () => {
    assert.equal(parseArgs(['--range=x..y', '--text-file=desc.md']).options.textFile, 'desc.md');
    assert.equal(parseArgs(['--range=x..y', '--text-file', 'desc.md']).options.textFile, 'desc.md');
  });

  it('accepts --report-dir=value and --report-dir value forms', () => {
    assert.equal(parseArgs(['--range=x..y', '--report-dir=out']).options.reportDir, 'out');
    assert.equal(parseArgs(['--range=x..y', '--report-dir', 'out']).options.reportDir, 'out');
  });

  it('rejects a missing --range even when --text-file is supplied', () => {
    const { ok, error } = parseArgs(['--text-file=desc.md']);
    assert.equal(ok, false);
    assert.match(error, /missing required --range/);
  });

  it('rejects a value-taking flag whose value is the next flag (no swallowing)', () => {
    const { ok, error } = parseArgs(['--range', '--text-file=desc.md']);
    assert.equal(ok, false);
    assert.match(error, /--range needs a revision range/);
  });

  it('rejects a value-taking flag with nothing after it', () => {
    assert.equal(parseArgs(['--range']).ok, false);
    assert.equal(parseArgs(['--text-file']).ok, false);
    assert.equal(parseArgs(['--report-dir']).ok, false);
  });

  it('rejects an unrecognized argument', () => {
    const { ok, error } = parseArgs(['--range=x..y', '--bogus']);
    assert.equal(ok, false);
    assert.match(error, /unrecognized argument: --bogus/);
  });

  it('short-circuits on --help, returning ok:true even if --range is missing', () => {
    const { ok, options } = parseArgs(['--help']);
    assert.ok(ok);
    assert.equal(options.help, true);
    assert.equal(options.range, undefined);
  });

  it('short-circuits on -h the same way', () => {
    const { ok, options } = parseArgs(['-h']);
    assert.ok(ok);
    assert.equal(options.help, true);
  });
});

// ---------------------------------------------------------------------------
// Sanity: parser exposes the two separators as the documented strings
// (test fixtures rely on them; if either were to drift the round-trip
// tests above would silently rot).
// ---------------------------------------------------------------------------

describe('separator constants', () => {
  it('FIELD_SEPARATOR is the ASCII Unit Separator (\\u001f)', () => {
    assert.equal(FIELD_SEPARATOR, '\u001f');
  });

  it('RECORD_SEPARATOR is the ASCII Record Separator (\\u001e)', () => {
    assert.equal(RECORD_SEPARATOR, '\u001e');
  });

  it('FIeld and REcord are different bytes — splitting records on the wrong one would silently merge', () => {
    assert.notEqual(FIELD_SEPARATOR, RECORD_SEPARATOR);
  });
});
