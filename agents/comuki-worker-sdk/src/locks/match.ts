import { globToRegExp } from './glob';
import type { LockRule } from './lock-rule';

/**
 * Matching helpers for lock descriptors. A rule only ever matches the subject
 * of its own `kind` — an `edit-path` rule never blocks a tool call and vice
 * versa. The `find*` helpers return the first matching rule (with its
 * human-readable `reason`) so a blocker can explain itself.
 */

export function lockMatchesPath(rule: LockRule, path: string): boolean {
  return rule.kind === 'edit-path' && globToRegExp(rule.pattern).test(normalizeSeparators(path));
}

export function lockMatchesTool(rule: LockRule, toolCall: string): boolean {
  return rule.kind === 'tool-name' && globToRegExp(rule.pattern, { segmentStars: false }).test(toolCall);
}

export function lockMatchesGitRef(rule: LockRule, gitRef: string): boolean {
  return rule.kind === 'git-ref' && globToRegExp(rule.pattern, { segmentStars: false }).test(gitRef);
}

export function findPathLock(rules: readonly LockRule[], path: string): LockRule | undefined {
  return rules.find((rule) => lockMatchesPath(rule, path));
}

export function findToolLock(rules: readonly LockRule[], toolCall: string): LockRule | undefined {
  return rules.find((rule) => lockMatchesTool(rule, toolCall));
}

export function findGitRefLock(rules: readonly LockRule[], gitRef: string): LockRule | undefined {
  return rules.find((rule) => lockMatchesGitRef(rule, gitRef));
}

function normalizeSeparators(path: string): string {
  return path.replaceAll('\\', '/');
}
