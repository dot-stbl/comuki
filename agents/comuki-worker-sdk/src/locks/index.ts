/**
 * Locks moved to `@comuki/agent-core` (locking semantics are shared between
 * the worker and dev SDKs; only the enforcement mechanics differ). This
 * module re-exports the previous public surface of
 * `@comuki/worker-sdk/locks/*` so existing imports keep working.
 */
export {
  BLOCKED_TOOL_TARGETS,
  findGitRefLock,
  findPathLock,
  findToolLock,
  globToRegExp,
  lockMatchesGitRef,
  lockMatchesPath,
  lockMatchesTool,
} from '@comuki/agent-core';

export type { GlobOptions, LockKind, LockRule } from '@comuki/agent-core';
