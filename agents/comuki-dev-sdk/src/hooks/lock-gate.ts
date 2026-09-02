/**
 * Claude Code PreToolUse lock gate.
 *
 * Reads the Claude Code hook payload from stdin (JSON: `tool_name`,
 * `tool_input`), decides allow/deny against the shared Comuki lock set
 * (`BLOCKED_TOOL_TARGETS` from @comuki/agent-core — the same semantics the
 * worker-sdk enforces via pi-extensions), and emits the decision in one of
 * two shapes behind a single emitter:
 *
 * - `json` (default) — stdout `{"hookSpecificOutput":{"hookEventName":
 *   "PreToolUse","permissionDecision":"deny",...}}`, exit 0. Requires
 *   Claude Code >= 1.0.84 (where `permissionDecision` was introduced);
 *   on older versions the JSON is ignored and the call is allowed.
 * - `exit` — exit code 2 + reason on stderr (blocks the tool call and feeds
 *   the reason to the model). Works on every Claude Code hooks version.
 *
 * Select with `COMUKI_HOOK_STYLE=json|exit` (default `json`).
 *
 * Fail-open: malformed stdin or an unrecognized payload shape allows the
 * call — a broken gate must not brick the developer's session; stderr
 * carries a note when that happens.
 */
import { BLOCKED_TOOL_TARGETS, findGitRefLock, findPathLock, findToolLock } from '@comuki/agent-core';
import type { LockRule } from '@comuki/agent-core';

export interface GateDecision {
  readonly decision: 'allow' | 'deny';
  readonly reason: string;
  readonly ruleId?: string;
}

export interface GateOutput {
  readonly exitCode: number;
  readonly stdout: string;
  readonly stderr: string;
}

export type HookStyle = 'json' | 'exit';

/** Claude Code tools whose primary argument is a file path. */
const EDIT_TOOLS = new Set(['Edit', 'Write', 'MultiEdit', 'NotebookEdit']);

/** `git` global flags that consume a value and therefore hide one token. */
const GIT_VALUE_FLAGS = new Set(['-C', '-c', '--git-dir', '--work-tree', '--namespace', '--config']);

interface HookPayload {
  readonly tool_name?: unknown;
  readonly tool_input?: {
    readonly file_path?: unknown;
    readonly notebook_path?: unknown;
    readonly command?: unknown;
  };
}

export function parseHookPayload(text: string): HookPayload | null {
  try {
    const parsed: unknown = JSON.parse(text);
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return null;
    }
    return parsed as HookPayload;
  } catch {
    return null;
  }
}

export function decideLockGate(payload: HookPayload | null, rules: readonly LockRule[] = BLOCKED_TOOL_TARGETS): GateDecision {
  if (payload === null) {
    return { decision: 'allow', reason: 'lock gate: unparseable hook payload, failing open' };
  }

  const toolName = payload.tool_name;
  const input = payload.tool_input ?? {};

  if (typeof toolName === 'string' && EDIT_TOOLS.has(toolName)) {
    const target = firstString(input.file_path, input.notebook_path);
    if (target !== undefined) {
      const lock = findPathLock(rules, target);
      if (lock !== undefined) {
        return deny(lock);
      }
    }
  }

  if (typeof toolName === 'string' && toolName === 'Bash') {
    const command = input.command;
    if (typeof command === 'string') {
      const toolLock = findToolLock(rules, `Bash(${command})`);
      if (toolLock !== undefined) {
        return deny(toolLock);
      }
      const gitLock = findGitPushLock(rules, command);
      if (gitLock !== undefined) {
        return deny(gitLock);
      }
    }
  }

  return { decision: 'allow', reason: 'no lock matched' };
}

function deny(rule: LockRule): GateDecision {
  return { decision: 'deny', reason: rule.reason, ruleId: rule.id };
}

function firstString(...values: readonly unknown[]): string | undefined {
  for (const value of values) {
    if (typeof value === 'string' && value.length > 0) {
      return value;
    }
  }
  return undefined;
}

/**
 * Extracts candidate git refs from a `git push` command. Best-effort: covers
 * global flags with values (`git -C dir push`), a remote before the refspecs
 * (`git push origin main`), bare refspecs (`git push origin HEAD:main` →
 * `main`) and fully-qualified refs. Returns refs in both bare and
 * `refs/heads/` forms so `git-ref` locks match either spelling.
 */
export function gitPushRefCandidates(command: string): string[] {
  const tokens = command.trim().split(/\s+/);
  if (tokens[0] !== 'git' && tokens[0] !== 'git.exe') {
    return [];
  }

  let index = 1;
  while (index < tokens.length && tokens[index] !== 'push') {
    const token = tokens[index] ?? '';
    if (token.startsWith('-')) {
      index++;
      if (GIT_VALUE_FLAGS.has(token)) {
        index++;
      }
      continue;
    }
    return [];
  }
  if (tokens[index] !== 'push') {
    return [];
  }

  const positionals = tokens
    .slice(index + 1)
    .map(stripQuotes)
    .filter((token) => token.length > 0 && !token.startsWith('-'));
  if (positionals.length === 0) {
    return [];
  }

  // With two or more positionals the first is the remote; with exactly one
  // it is the refspec (the remote comes from the git config).
  const refspecs = positionals.length >= 2 ? positionals.slice(1) : positionals;

  const candidates: string[] = [];
  for (const refspec of refspecs) {
    const destination = refspec.includes(':') ? (refspec.split(':').pop() ?? '') : refspec;
    if (destination.length === 0) {
      continue;
    }
    candidates.push(destination);
    candidates.push(destination.startsWith('refs/') ? destination : `refs/heads/${destination}`);
  }
  return candidates;
}

function stripQuotes(token: string): string {
  const trimmed = token.trim();
  if (trimmed.length >= 2) {
    const first = trimmed.charAt(0);
    const last = trimmed.charAt(trimmed.length - 1);
    if ((first === '"' && last === '"') || (first === "'" && last === "'")) {
      return trimmed.slice(1, -1);
    }
  }
  return trimmed;
}

function findGitPushLock(rules: readonly LockRule[], command: string): LockRule | undefined {
  // A compound shell line can hide the push (`cd pkg && git push origin main`),
  // so every command segment is inspected, not just the first.
  for (const segment of command.split(/&&|\|\||;|\||\n|\r/)) {
    for (const candidate of gitPushRefCandidates(segment)) {
      const lock = findGitRefLock(rules, candidate);
      if (lock !== undefined) {
        return lock;
      }
    }
  }
  return undefined;
}

export function hookStyleFromEnv(value: string | undefined): HookStyle {
  return value === 'exit' ? 'exit' : 'json';
}

/** The one emitter both output shapes live behind. */
export function emitDecision(gate: GateDecision, style: HookStyle): GateOutput {
  if (gate.decision === 'allow') {
    return { exitCode: 0, stdout: '', stderr: '' };
  }
  if (style === 'exit') {
    return { exitCode: 2, stdout: '', stderr: gate.reason };
  }
  return {
    exitCode: 0,
    stdout: JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: 'deny',
        permissionDecisionReason: gate.reason,
      },
    }),
    stderr: '',
  };
}

/** Pure pipeline: stdin text + env → output. Test-friendly; the CLI is a thin wrapper. */
export function runLockGate(
  stdinText: string,
  env: Record<string, string | undefined> = process.env,
  rules: readonly LockRule[] = BLOCKED_TOOL_TARGETS,
): GateOutput {
  return emitDecision(decideLockGate(parseHookPayload(stdinText), rules), hookStyleFromEnv(env.COMUKI_HOOK_STYLE));
}

if (import.meta.main) {
  const output = runLockGate(await Bun.stdin.text());
  if (output.stdout.length > 0) {
    process.stdout.write(`${output.stdout}\n`);
  }
  if (output.stderr.length > 0) {
    process.stderr.write(`${output.stderr}\n`);
  }
  process.exit(output.exitCode);
}
