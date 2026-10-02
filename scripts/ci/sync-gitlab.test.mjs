import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { resolve } from 'node:path';
import {
  classifyPour,
  overlayTouched,
  overlayPaths,
  parseArgs,
  pourAction,
  renderPlan,
  restoreOverlayCommitMessage,
  stripOverlayCommitMessage,
} from './sync-gitlab.mjs';

describe('parseArgs', () => {
  it('defaults to dry-run github/master → gitlab/master', () => {
    const parsed = parseArgs([]);
    assert.equal(parsed.ok, true);
    assert.equal(parsed.options.apply, false);
    assert.equal(parsed.options.source, 'github/master');
    assert.equal(parsed.options.target, 'gitlab/master');
    assert.equal(parsed.options.pushRemote, 'gitlab');
    assert.equal(parsed.options.pushRef, 'master');
    assert.equal(parsed.options.force, undefined);
  });

  it('accepts --apply and value flags', () => {
    const parsed = parseArgs(['--apply', '--source', 'github/feature', '--push-ref=hybrid']);
    assert.equal(parsed.ok, true);
    assert.equal(parsed.options.apply, true);
    assert.equal(parsed.options.source, 'github/feature');
    assert.equal(parsed.options.pushRef, 'hybrid');
  });

  it('rejects unknown flags and missing values', () => {
    assert.equal(parseArgs(['--force']).ok, false);
    assert.equal(parseArgs(['--source']).ok, false);
  });

  it('flips direction for --to-github and strips overlay', () => {
    const parsed = parseArgs(['--to-github']);
    assert.equal(parsed.ok, true);
    assert.equal(parsed.options.source, 'gitlab/master');
    assert.equal(parsed.options.target, 'github/master');
    assert.equal(parsed.options.pushRemote, 'github');
    assert.equal(parsed.options.stripOverlay, true);
    assert.match(parsed.options.worktree, /sync-github$/);
  });

  it('resolves --repo and default worktree', () => {
    const parsed = parseArgs(['--repo', '/tmp/comuki']);
    assert.equal(parsed.ok, true);
    assert.equal(parsed.options.repoRoot, resolve('/tmp/comuki'));
    assert.equal(parsed.options.worktree, resolve('/tmp/comuki/.agents/worktree/sync-gitlab'));
  });
});

describe('classifyPour / pourAction', () => {
  it('identical → noop', () => {
    assert.equal(classifyPour({ sourceAhead: 0, targetAhead: 0 }), 'identical');
    assert.equal(pourAction('identical'), 'noop');
  });

  it('github ahead, gitlab even → fast-forward merge', () => {
    assert.equal(classifyPour({ sourceAhead: 4, targetAhead: 0 }), 'fast-forward');
    assert.equal(pourAction('fast-forward'), 'merge-source-into-target');
  });

  it('gitlab ahead, github even → warn, do not rewind gitlab', () => {
    assert.equal(classifyPour({ sourceAhead: 0, targetAhead: 1400 }), 'catch-up-target');
    assert.equal(pourAction('catch-up-target'), 'noop-warn-github-behind');
  });

  it('diverged → merge github onto gitlab, keep gitlab unique', () => {
    assert.equal(classifyPour({ sourceAhead: 12, targetAhead: 1400 }), 'diverged');
    assert.equal(pourAction('diverged'), 'merge-source-into-target');
  });
});

describe('overlay restore', () => {
  it('detects overlay paths including nested hybrid files', () => {
    assert.equal(overlayTouched(['platform/src/Host.cs']), false);
    assert.equal(overlayTouched(['.gitlab-ci.yml']), true);
    assert.equal(overlayTouched(['deploy/hybrid/ci.yml']), true);
    assert.deepEqual(overlayPaths, ['.gitlab-ci.yml', 'deploy/hybrid']);
  });

  it('restore and strip commits use the house format', () => {
    assert.match(restoreOverlayCommitMessage(), /^\[\.stbl\]\(feat\/meta\): /);
    assert.match(stripOverlayCommitMessage(), /^\[\.stbl\]\(feat\/meta\): /);
  });
});

describe('renderPlan', () => {
  it('warns when github is behind and lists unique gitlab commits', () => {
    const text = renderPlan({
      source: 'github/master',
      target: 'gitlab/master',
      sourceSha: 'aaa',
      targetSha: 'bbb',
      sourceAhead: 0,
      targetAhead: 3,
      kind: 'catch-up-target',
      action: 'noop-warn-github-behind',
      overlayPaths,
      sourceUnique: [],
      targetUnique: ['bbb keep overlay'],
      push: 'gitlab master',
      force: false,
    });
    assert.match(text, /nothing to pour onto GitLab/);
    assert.match(text, /github is behind/);
    assert.match(text, /force: never/);
  });
});
