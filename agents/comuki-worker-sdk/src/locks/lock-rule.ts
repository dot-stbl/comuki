/**
 * A lock descriptor — pure data describing one hard restriction for a worker.
 * Locks are enforced by runtime adapters (`pi-extensions` for workers, Claude
 * Code `hooks` for devs); this module only describes them so both adapters
 * share one vocabulary.
 *
 * - `edit-path` — pattern matched against file paths the agent wants to edit.
 * - `tool-name` — pattern matched against tool calls rendered as
 *   `Tool(<primary-arg-prefix>)`, e.g. `Bash(npm install left-pad)`.
 * - `git-ref` — pattern matched against the full git ref, e.g. `refs/heads/main`.
 */
export type LockKind = 'edit-path' | 'tool-name' | 'git-ref';

export interface LockRule {
  readonly id: string;
  readonly kind: LockKind;
  readonly pattern: string;
  readonly reason: string;
}
