/**
 * Tests for scripts/commit-lint.mjs — `node --test scripts/commit-lint.test.mjs`.
 * Zero dependencies: node:test + node:assert/strict only.
 */
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  COMMIT_TYPES,
  SUBJECT_MAX_LENGTH,
  extractSubject,
  lintMessage,
  lintSubject,
  stripAttribution,
} from './commit-lint.mjs';

/** Assert the subject passes, with the problems in the failure message. */
function assertClean(subject) {
  const problems = lintSubject(subject);
  assert.deepEqual(problems, [], `expected clean subject: ${subject}\n  ${problems.join('\n  ')}`);
}

function assertRejected(subject) {
  assert.ok(lintSubject(subject).length > 0, `expected a rejection for: ${subject}`);
}

/** Assert the whole line disappears from a realistic footer. */
function assertLineStripped(line) {
  const message = `[.stbl] feat(x): do the thing\n\nSome body text.\n\n${line}\n`;
  const { text, removed } = stripAttribution(message);
  assert.equal(removed.length, 1, `expected exactly one removal for: ${line}`);
  assert.equal(removed[0], line.trim());
  assert.equal(text, '[.stbl] feat(x): do the thing\n\nSome body text.\n');
}

// ---------------------------------------------------------------------------

describe('lintSubject — good examples from commit-format.md', () => {
  const good = [
    '[.stbl] feat(orchestration): add claim/lease loop for pull-queue',
    '[.stbl] fix(database): correct cascade delete on runs table',
    '[.stbl] docs(roadmap): clarify Slice 0 DoD with idempotency check',
    '[.stbl] chore(deps): bump dotnet to 10.0.108',
    '[.stbl] chore(rules): adopt [.stbl] prefix for comuki commits',
    '[.stbl] refactor(translator): extract stream-json parser into separate file',
    '[.stbl] test(orchestration): cover two-claimer race for FOR UPDATE SKIP LOCKED',
    '[.stbl] ci(be): enforce extended analyzer rules in build-verification',
    '[.stbl] feat(api): change /tasks response shape',
    '[.stbl] fix(orchestration): make claim transaction atomic with lease insert',
  ];

  for (const subject of good) {
    it(subject, () => assertClean(subject));
  }
});

describe('lintSubject — bad examples from commit-format.md', () => {
  const bad = [
    'feat(orchestration): add foo',
    'feat: add foo',
    '[stbl](feat): add foo',
    '[hybrid](feat/dashboard): add foo',
    '[.stbl](feat/Orchestration): add foo',
    '[.stbl](feat): add foo',
    '[.stbl] feat() add foo',
    'feat: Added new endpoint.',
    'WIP',
    'feat add foo',
    'update stuff',
  ];

  for (const subject of bad) {
    it(subject, () => assertRejected(subject));
  }

  // Both retired prefixes get named rather than folded into "missing prefix":
  // every commit in this history carries one of them, so the author is told
  // which one they reached for instead of being told they wrote nothing.
  for (const [retired, pattern] of [
    ['[stbl](feat/x): add foo', /`\[stbl\]` is retired/],
    ['[hybrid] feat(x): add foo', /`\[hybrid\]` is retired/],
  ]) {
    it(`names ${retired.slice(0, retired.indexOf(']') + 1)} as retired`, () => {
      const report = lintSubject(retired).join('\n');
      assert.match(report, pattern);
      assert.match(report, /this repo uses `\[\.stbl\]`/);
    });
  }

  it('tells a root-only feature path what it is missing', () => {
    assert.match(
      lintSubject('[.stbl](feat): add foo').join('\n'),
      /names a root with no area/
    );
  });

  it('suggests the prefixed form when only the prefix is missing', () => {
    assert.deepEqual(lintSubject('feat: add foo'), [
      'missing `[.stbl]` prefix — write `[.stbl] feat: add foo`',
    ]);
  });

  it('reports every problem at once on an unprefixed conventional subject', () => {
    const report = lintSubject('feat: Added new endpoint.').join('\n');

    assert.match(report, /missing `\[\.stbl\]` prefix/);
    assert.match(report, /must not end with/);
  });
});

describe('lintSubject — format contract', () => {
  it('accepts every allowed type without a scope', () => {
    for (const type of COMMIT_TYPES) {
      assertClean(`[.stbl] ${type}: do the thing`);
    }
  });

  it('accepts the breaking-change marker, with and without a scope', () => {
    assertClean('[.stbl] feat!: drop the legacy claim endpoint');
    assertClean('[.stbl] feat(api)!: drop the legacy claim endpoint');
  });

  it('accepts scopes with dots, slashes, dashes and digits', () => {
    assertClean('[.stbl] chore(agents/dev-sdk): wire the installer');
    assertClean('[.stbl] build(net10.0): bump the sdk pin');
  });

  it('rejects an unknown type', () => {
    assert.match(lintSubject('[.stbl] cleanup: drop the scratch files').join('\n'), /unknown type/);
  });

  it('accepts a hand-written merge commit', () => {
    assertClean('[.stbl] merge(readme): OSS landing page');
    assertClean('[.stbl] merge(oss-deploy): self-hosting artifacts — compose + helm + k8s');
    assertClean('[.stbl] merge: sync github pr #55 (mask .net surface) into hybrid contour');
  });

  it('does not police the case of the description', () => {
    // Identifiers and acronyms open a description all the time — the written
    // rule asks for an imperative, not a lowercase, description.
    assertClean('[.stbl] fix(host): SubjectScopeMiddleware wraps PermissionEvaluator in AsSystem');
    assertClean('[.stbl] feat(deploy): OSS deployment artifacts — docker-compose + helm');
    assertClean('[.stbl] fix(cve): CVE-2026-49451 bump plus 9 captive singletons');
    assertClean('[.stbl] feat(secrets): VaultSecretProvider — Slice 2 of issue #52');
  });

  it('rejects an uppercase type and suggests the lowercase one', () => {
    assert.match(lintSubject('[.stbl] Feat(api): add thing').join('\n'), /must be lowercase/);
  });

  it('rejects an empty scope', () => {
    assert.match(lintSubject('[.stbl] feat(): add thing').join('\n'), /empty scope/);
  });

  it('rejects an uppercase scope', () => {
    assert.match(lintSubject('[.stbl] feat(Orchestration): add thing').join('\n'), /scope/);
  });

  it('rejects a trailing period', () => {
    assert.match(lintSubject('[.stbl] feat(api): add thing.').join('\n'), /must not end with/);
  });

  it('rejects an empty description', () => {
    assert.ok(lintSubject('[.stbl] feat(api): ').length > 0);
  });

  it('rejects a double space after the colon', () => {
    assert.match(lintSubject('[.stbl] feat(api):  add thing').join('\n'), /one space/);
  });

  it(`rejects a subject longer than ${SUBJECT_MAX_LENGTH} chars`, () => {
    const tooLong = `[.stbl] feat(api): ${'a'.repeat(SUBJECT_MAX_LENGTH)}`;
    assert.match(lintSubject(tooLong).join('\n'), new RegExp(`limit is ${SUBJECT_MAX_LENGTH}`));
  });

  it(`accepts a subject of exactly ${SUBJECT_MAX_LENGTH} chars`, () => {
    const head = '[.stbl] feat(api): ';
    assertClean(head + 'a'.repeat(SUBJECT_MAX_LENGTH - head.length));
  });

  it('rejects an empty message', () => {
    assert.ok(lintSubject('').length > 0);
  });

  it('tolerates trailing whitespace on the subject line', () => {
    assertClean('[.stbl] feat(api): add thing   ');
  });
});

describe('lintSubject — git-generated subjects are exempt', () => {
  const exempt = [
    "Merge branch 'master' into feat/commit-gate",
    'Merge pull request #42 from hybrid/feat-x',
    'Merge remote-tracking branch \'origin/master\'',
    'Revert "[.stbl] feat(api): add thing"',
    'fixup! [.stbl] feat(api): add thing',
    'squash! [.stbl] feat(api): add thing',
  ];

  for (const subject of exempt) {
    it(subject, () => assertClean(subject));
  }

  it('does not exempt a lowercase "merge stuff"', () => {
    assertRejected('merge stuff');
  });

  it('still accepts revert as a normal type', () => {
    assertClean('[.stbl] revert(api): undo the claim endpoint change');
  });
});

// ---------------------------------------------------------------------------

describe('stripAttribution — whole-line co-author trailers', () => {
  const trailers = [
    'Co-Authored-By: Claude <noreply@anthropic.com>',
    'Co-authored-by: Claude Opus 5 (1M context) <noreply@anthropic.com>',
    'Co-Authored-By: ChatGPT <bot@example.com>',
    'Co-authored-by: OpenAI Codex <codex@example.com>',
    'Co-Authored-By: GPT-4 <gpt@example.com>',
    'Co-authored-by: Copilot <copilot@github.com>',
    'Co-Authored-By: Cursor Agent <agent@cursor.com>',
    'Co-authored-by: Gemini <gemini@example.com>',
    'Co-Authored-By: opencode <o@example.com>',
    'Co-authored-by: Devin <devin@example.com>',
    'Co-Authored-By: aider <aider@example.com>',
    'Co-authored-by: Windsurf <w@example.com>',
    // display name looks human — the email gives it away
    'Co-Authored-By: Jane Doe <jane@anthropic.com>',
    'Co-authored-by: Build Bot <bot@cursor.sh>',
  ];

  for (const line of trailers) {
    it(line, () => assertLineStripped(line));
  }

  it('keeps a genuine human co-author', () => {
    const message = '[.stbl] feat(x): do the thing\n\nCo-Authored-By: Jane Doe <jane@hybrid.ai>\n';
    const { text, removed } = stripAttribution(message);
    assert.deepEqual(removed, []);
    assert.equal(text, message);
  });
});

describe('stripAttribution — generated-with and authorship prose', () => {
  const lines = [
    '🤖 Generated with [Claude Code](https://claude.com/claude-code)',
    'Generated with [Claude Code](https://claude.ai/code)',
    'Generated with Claude Code',
    'Co-authored by Claude',
    'Co-Authored-By Claude Code',
    'Authored with Codex',
    'Written by Claude',
    'Designed by Gemini',
    'Created with Copilot',
    'Built by Cursor',
    'Made with Claude',
    'Assisted-By: Claude <noreply@anthropic.com>',
    'AI-generated',
    'AI-assisted',
    'AI-authored',
    'AI generated commit message',
  ];

  for (const line of lines) {
    it(line, () => assertLineStripped(line));
  }
});

describe('stripAttribution — inline excision', () => {
  it('keeps the real text and drops the footer fragment', () => {
    const { text, removed } = stripAttribution(
      '[.stbl] feat(x): do the thing\n\nRefs: COM-142 🤖 Generated with [Claude Code](https://claude.com/claude-code)\n',
    );
    assert.equal(text, '[.stbl] feat(x): do the thing\n\nRefs: COM-142\n');
    assert.equal(removed.length, 1);
    assert.match(removed[0], /Generated with/);
  });

  it('drops a bare noreply@anthropic.com without gluing the words together', () => {
    const { text, removed } = stripAttribution(
      '[.stbl] feat(x): do the thing\n\nPing noreply@anthropic.com for details\n',
    );
    assert.equal(text, '[.stbl] feat(x): do the thing\n\nPing for details\n');
    assert.deepEqual(removed, ['noreply@anthropic.com']);
  });

  it('drops an angle-bracketed address from a non-co-author trailer', () => {
    const { text } = stripAttribution(
      '[.stbl] feat(x): do the thing\n\nReported-by: bot <noreply@anthropic.com>\n',
    );
    assert.equal(text, '[.stbl] feat(x): do the thing\n\nReported-by: bot\n');
  });

  it('drops the line entirely when nothing real is left on it', () => {
    const { text, removed } = stripAttribution(
      '[.stbl] feat(x): do the thing\n\nbody\n\n   noreply@anthropic.com   \n',
    );
    assert.equal(text, '[.stbl] feat(x): do the thing\n\nbody\n');
    assert.equal(removed.length, 1);
  });
});

describe('stripAttribution — false positives', () => {
  const innocent = [
    'The migration generated 42 rows in one pass.',
    'This client was generated by the Kubb codegen.',
    'Auto-generated API client is regenerated on every build.',
    'regenerated with the new tool after the schema change',
    'Built with love by the platform team.',
    'Docs mention Claude Code as an integration target, see .agents/docs/architecture/.',
    'Add the anthropic provider adapter to the translator.',
    'Co-Authored-By: Jane Doe <jane@hybrid.ai>',
  ];

  for (const line of innocent) {
    it(line, () => {
      const message = `[.stbl] feat(x): do the thing\n\n${line}\n`;
      const { text, removed } = stripAttribution(message);
      assert.deepEqual(removed, [], `unexpectedly stripped: ${line}`);
      assert.equal(text, message);
    });
  }
});

describe('stripAttribution — trailing blank run and idempotence', () => {
  it('collapses the blank run the removed trailer block leaves behind', () => {
    const { text } = stripAttribution(
      [
        '[.stbl] feat(x): do the thing',
        '',
        'Why: the claim loop raced with the lease insert.',
        '',
        '🤖 Generated with [Claude Code](https://claude.com/claude-code)',
        '',
        'Co-Authored-By: Claude <noreply@anthropic.com>',
        '',
      ].join('\n'),
    );
    assert.equal(
      text,
      '[.stbl] feat(x): do the thing\n\nWhy: the claim loop raced with the lease insert.\n',
    );
  });

  it('collapses the blank run above the git comment template too', () => {
    const { text } = stripAttribution(
      [
        '[.stbl] feat(hooks): add commit-msg gate',
        '',
        'Why: the format rule was honour-system only.',
        '',
        '🤖 Generated with [Claude Code](https://claude.com/claude-code)',
        '',
        'Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>',
        '',
        '# Please enter the commit message for your changes.',
        '# On branch feat/commit-gate',
        '',
      ].join('\n'),
    );

    assert.equal(
      text,
      [
        '[.stbl] feat(hooks): add commit-msg gate',
        '',
        'Why: the format rule was honour-system only.',
        '',
        '# Please enter the commit message for your changes.',
        '# On branch feat/commit-gate',
        '',
      ].join('\n'),
    );
  });

  it('keeps one blank line above the scissors block', () => {
    const { text } = stripAttribution(
      [
        '[.stbl] feat(x): do the thing',
        '',
        'Co-Authored-By: Claude <noreply@anthropic.com>',
        '',
        '# ------------------------ >8 ------------------------',
        'diff --git a/x b/x',
        '',
      ].join('\n'),
    );

    assert.equal(
      text,
      [
        '[.stbl] feat(x): do the thing',
        '',
        '# ------------------------ >8 ------------------------',
        'diff --git a/x b/x',
        '',
      ].join('\n'),
    );
  });

  it('is idempotent — running it twice changes nothing', () => {
    const original = [
      '[.stbl] feat(x): do the thing',
      '',
      'Body paragraph.',
      '',
      '🤖 Generated with [Claude Code](https://claude.com/claude-code)',
      '',
      'Co-Authored-By: Claude <noreply@anthropic.com>',
      '',
    ].join('\n');

    const once = stripAttribution(original);
    const twice = stripAttribution(once.text);

    assert.equal(twice.text, once.text);
    assert.deepEqual(twice.removed, []);
  });

  it('leaves a clean message byte-identical', () => {
    const clean = '[.stbl] fix(database): correct cascade delete on runs table\n\nBody.\n';
    const { text, removed } = stripAttribution(clean);
    assert.equal(text, clean);
    assert.deepEqual(removed, []);
  });

  it('leaves a clean message with trailing blank lines alone', () => {
    const clean = '[.stbl] fix(database): correct cascade delete\n\n\n';
    assert.equal(stripAttribution(clean).text, clean);
  });

  it('survives CRLF line endings', () => {
    const { text, removed } = stripAttribution(
      '[.stbl] feat(x): do the thing\r\n\r\nBody.\r\n\r\nCo-Authored-By: Claude <noreply@anthropic.com>\r\n',
    );
    assert.equal(text, '[.stbl] feat(x): do the thing\r\n\r\nBody.\r\n');
    assert.equal(removed.length, 1);
  });

  it('can empty a message that was nothing but attribution', () => {
    const { text, removed } = stripAttribution('Co-Authored-By: Claude <noreply@anthropic.com>\n');
    assert.equal(text, '\n');
    assert.equal(removed.length, 1);
  });
});

describe('stripAttribution / extractSubject — # comments', () => {
  const gitTemplate = [
    '[.stbl] feat(x): do the thing',
    '',
    '# Please enter the commit message for your changes. Lines starting',
    "# with '#' will be ignored, and an empty message aborts the commit.",
    '#',
    '# On branch feat/commit-gate',
  ].join('\n');

  it('finds the subject above the comment block', () => {
    assert.equal(extractSubject(gitTemplate), '[.stbl] feat(x): do the thing');
  });

  it('skips leading comments and blank lines when locating the subject', () => {
    const message = '# a template header\n\n\n[.stbl] docs(rules): add the gate\n';
    assert.equal(extractSubject(message), '[.stbl] docs(rules): add the gate');
  });

  it('never strips attribution out of a # comment line', () => {
    const message =
      '[.stbl] feat(x): do the thing\n\n# Co-Authored-By: Claude <noreply@anthropic.com>\n# 🤖 Generated with [Claude Code](https://claude.com/claude-code)\n';
    const { text, removed } = stripAttribution(message);
    assert.deepEqual(removed, []);
    assert.equal(text, message);
  });

  it('never touches the --verbose diff below the scissors marker', () => {
    const message = [
      '[.stbl] feat(x): do the thing',
      '',
      'Co-Authored-By: Claude <noreply@anthropic.com>',
      '# ------------------------ >8 ------------------------',
      '# Do not modify or remove the line above.',
      'diff --git a/rule.md b/rule.md',
      '+Co-Authored-By: Claude <noreply@anthropic.com>',
      '',
    ].join('\n');

    const { text, removed } = stripAttribution(message);

    assert.equal(removed.length, 1);
    assert.ok(text.includes('+Co-Authored-By: Claude <noreply@anthropic.com>'));
    assert.ok(text.includes('# ------------------------ >8 ------------------------'));
    assert.equal(extractSubject(text), '[.stbl] feat(x): do the thing');
  });

  it('returns an empty subject for a comment-only message', () => {
    assert.equal(extractSubject('# nothing here\n#\n'), '');
  });
});

// ---------------------------------------------------------------------------

describe('lintMessage', () => {
  it('reports a clean message as clean', () => {
    const result = lintMessage('[.stbl] fix(database): correct cascade delete on runs table\n\nWhy.\n');
    assert.deepEqual(result.problems, []);
    assert.deepEqual(result.attribution, []);
    assert.equal(result.subject, '[.stbl] fix(database): correct cascade delete on runs table');
  });

  it('reports attribution and subject problems independently', () => {
    const result = lintMessage(
      'feat: Added new endpoint.\n\nCo-Authored-By: Claude <noreply@anthropic.com>\n',
    );
    assert.equal(result.attribution.length, 1);
    assert.ok(result.problems.length > 0);
    assert.equal(result.subject, 'feat: Added new endpoint.');
  });

  it('lints the subject of the stripped message, not the original', () => {
    const result = lintMessage(
      '🤖 Generated with [Claude Code](https://claude.com/claude-code)\n[.stbl] feat(x): do the thing\n',
    );
    assert.equal(result.subject, '[.stbl] feat(x): do the thing');
    assert.deepEqual(result.problems, []);
  });
});
