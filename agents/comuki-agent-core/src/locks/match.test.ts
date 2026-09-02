import { describe, expect, test } from 'bun:test';
import { globToRegExp } from './glob';
import type { LockRule } from './lock-rule';
import { findGitRefLock, findPathLock, findToolLock, lockMatchesGitRef, lockMatchesPath, lockMatchesTool } from './match';

describe('globToRegExp', () => {
  test('single star stays within one segment', () => {
    const pattern = globToRegExp('src/*.ts');

    expect(pattern.test('src/foo.ts')).toBe(true);
    expect(pattern.test('src/a/b/foo.ts')).toBe(false);
  });

  test('double star spans segments and may match zero directories', () => {
    const pattern = globToRegExp('**/*.test.ts');

    expect(pattern.test('foo.test.ts')).toBe(true);
    expect(pattern.test('a/b/foo.test.ts')).toBe(true);
    expect(pattern.test('foo.ts')).toBe(false);
  });

  test('double star at the tail matches any depth', () => {
    const pattern = globToRegExp('**/tests/**');

    expect(pattern.test('tests/foo.ts')).toBe(true);
    expect(pattern.test('a/b/tests/c/d.ts')).toBe(true);
    expect(pattern.test('a/bests/c.ts')).toBe(false);
  });

  test('escapes regex metacharacters', () => {
    const pattern = globToRegExp('Bash(npm install*', { segmentStars: false });

    expect(pattern.test('Bash(npm install left-pad')).toBe(true);
    expect(pattern.test('BashXnpm install left-pad')).toBe(false);
  });

  test('question mark matches one character', () => {
    const pattern = globToRegExp('?.ts');

    expect(pattern.test('a.ts')).toBe(true);
    expect(pattern.test('ab.ts')).toBe(false);
  });

  test('star extension matches any test-file extension', () => {
    const pattern = globToRegExp('**/*.test.*');

    expect(pattern.test('foo.test.ts')).toBe(true);
    expect(pattern.test('foo.test.tsx')).toBe(true);
    expect(pattern.test('src/foo.test.js')).toBe(true);
    expect(pattern.test('foo.ts')).toBe(false);
  });
});

const editRule: LockRule = {
  id: 'test-no-edit-tests',
  kind: 'edit-path',
  pattern: '**/*.test.ts',
  reason: 'tests are platform-owned',
};
const toolRule: LockRule = {
  id: 'test-no-bun-add',
  kind: 'tool-name',
  pattern: 'Bash(bun add*',
  reason: 'no side installs',
};
const refRule: LockRule = {
  id: 'test-no-push-main',
  kind: 'git-ref',
  pattern: 'refs/heads/main',
  reason: 'no direct pushes',
};

describe('lock matching', () => {
  test('edit-path rules match nested and root-level paths', () => {
    expect(lockMatchesPath(editRule, 'foo.test.ts')).toBe(true);
    expect(lockMatchesPath(editRule, 'src/deep/foo.test.ts')).toBe(true);
    expect(lockMatchesPath(editRule, 'src/foo.ts')).toBe(false);
  });

  test('edit-path rules normalize windows separators', () => {
    expect(lockMatchesPath(editRule, 'src\\deep\\foo.test.ts')).toBe(true);
  });

  test('tool-name rules match tool calls with argument prefixes', () => {
    expect(lockMatchesTool(toolRule, 'Bash(bun add zod)')).toBe(true);
    expect(lockMatchesTool(toolRule, 'Bash(bun add @scope/pkg)')).toBe(true);
    expect(lockMatchesTool(toolRule, 'Bash(bun run test)')).toBe(false);
  });

  test('git-ref rules match the full ref only', () => {
    expect(lockMatchesGitRef(refRule, 'refs/heads/main')).toBe(true);
    expect(lockMatchesGitRef(refRule, 'refs/heads/feature/x')).toBe(false);
    expect(lockMatchesGitRef(refRule, 'main')).toBe(false);
  });

  test('rules never match a subject of another kind', () => {
    expect(lockMatchesPath(toolRule, 'Bash(bun add zod)')).toBe(false);
    expect(lockMatchesTool(editRule, 'src/foo.test.ts')).toBe(false);
    expect(lockMatchesGitRef(toolRule, 'refs/heads/main')).toBe(false);
  });
});

describe('find*Lock helpers', () => {
  const rules: readonly LockRule[] = [editRule, toolRule, refRule];

  test('findPathLock returns the first matching rule', () => {
    expect(findPathLock(rules, 'src/foo.test.ts')?.id).toBe('test-no-edit-tests');
    expect(findPathLock(rules, 'src/foo.ts')).toBeUndefined();
  });

  test('findToolLock returns the first matching rule', () => {
    expect(findToolLock(rules, 'Bash(bun add left-pad)')?.id).toBe('test-no-bun-add');
    expect(findToolLock(rules, 'Read')).toBeUndefined();
  });

  test('findGitRefLock returns the first matching rule', () => {
    expect(findGitRefLock(rules, 'refs/heads/main')?.id).toBe('test-no-push-main');
    expect(findGitRefLock(rules, 'refs/heads/dev')).toBeUndefined();
  });
});
