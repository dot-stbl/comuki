import { describe, expect, test } from 'bun:test';
import { BLOCKED_TOOL_TARGETS } from './blocked-targets';
import { findGitRefLock, findPathLock, findToolLock } from './match';

describe('BLOCKED_TOOL_TARGETS', () => {
  test('ids are unique', () => {
    const ids = BLOCKED_TOOL_TARGETS.map((rule) => rule.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  test('every rule is fully populated', () => {
    for (const rule of BLOCKED_TOOL_TARGETS) {
      expect(rule.id.length).toBeGreaterThan(0);
      expect(rule.pattern.length).toBeGreaterThan(0);
      expect(rule.reason.length).toBeGreaterThan(0);
    }
  });

  test('blocks editing test files', () => {
    expect(findPathLock(BLOCKED_TOOL_TARGETS, 'src/foo.test.ts')).toBeDefined();
    expect(findPathLock(BLOCKED_TOOL_TARGETS, 'src/foo.spec.tsx')).toBeDefined();
    expect(findPathLock(BLOCKED_TOOL_TARGETS, 'tests/unit/parser.test.ts')).toBeDefined();
    expect(findPathLock(BLOCKED_TOOL_TARGETS, 'src/foo.ts')).toBeUndefined();
  });

  test('blocks package installs', () => {
    expect(findToolLock(BLOCKED_TOOL_TARGETS, 'Bash(npm install zod)')).toBeDefined();
    expect(findToolLock(BLOCKED_TOOL_TARGETS, 'Bash(bun add @types/bun)')).toBeDefined();
    expect(findToolLock(BLOCKED_TOOL_TARGETS, 'Bash(pip install requests)')).toBeDefined();
    expect(findToolLock(BLOCKED_TOOL_TARGETS, 'Bash(dotnet add package Newtonsoft.Json)')).toBeDefined();
    expect(findToolLock(BLOCKED_TOOL_TARGETS, 'Bash(npm run build)')).toBeUndefined();
  });

  test('blocks pushing to protected branches', () => {
    expect(findGitRefLock(BLOCKED_TOOL_TARGETS, 'refs/heads/main')).toBeDefined();
    expect(findGitRefLock(BLOCKED_TOOL_TARGETS, 'refs/heads/master')).toBeDefined();
    expect(findGitRefLock(BLOCKED_TOOL_TARGETS, 'refs/heads/feature/agents-sdk')).toBeUndefined();
  });
});
