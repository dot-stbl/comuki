/**
 * Pi-extension enforcing the platform locks at tool-call time.
 *
 * Locks are described by data (`LockRule` from `@comuki/agent-core`) — this
 * module is the adapter that turns that data into actual denials. The
 * matching helpers live in agent-core so the dev-sdk and the worker-sdk
 * share the same semantics; only the enforcement mechanics differ
 * (Claude Code hooks vs. pi tool_call handlers).
 *
 * Spec (openspec/changes/harden-pi-worker-sandbox §6.1): default and
 * profile locks SHALL be enforced by a pi-extension at tool-call time.
 * Files on disk describing locks are not enforcement — the gate is the
 * extension's handler. A matching lock denies the call and surfaces its
 * `reason`.
 */
import {
  BLOCKED_TOOL_TARGETS,
  findGitRefLock,
  findPathLock,
  findToolLock,
} from "@comuki/agent-core"
import type { LockRule } from "@comuki/agent-core"
import type {
  ExtensionFactory,
  PiExtensionContext,
  PiExtensionApi,
  PiToolCallEvent,
} from "./api"

export interface LocksExtensionOptions {
  /** Profile-specific rules appended to the platform default. */
  readonly profileLocks?: readonly LockRule[]
}

/** Built-in pi tools whose primary argument is a file path. */
const PATH_TOOLS: ReadonlySet<string> = new Set(["write", "edit"])

/**
 * Factory for the locks extension. The returned function has the shape pi
 * expects (`ExtensionFactory`) — pass it to pi's loader or wrap it as a
 * default-exported file in the project's `.pi/extensions/`.
 */
export function createLocksExtension(
  options: LocksExtensionOptions = {}
): ExtensionFactory {
  const rules: readonly LockRule[] = options.profileLocks === undefined
    ? BLOCKED_TOOL_TARGETS
    : [...BLOCKED_TOOL_TARGETS, ...options.profileLocks]

  return function locksExtension(api: PiExtensionApi): void {
    api.on("tool_call", (event) => enforceLocks(event, rules))
  }
}

function enforceLocks(
  event: PiToolCallEvent,
  rules: readonly LockRule[]
): { block: true; reason: string } | undefined {
  const toolName = event.toolName
  const input = event.input

  if (toolName === "write" || toolName === "edit") {
    const target = pathArgument(input)
    if (target !== undefined) {
      const lock = findPathLock(rules, target)
      if (lock !== undefined) {
        return { block: true, reason: lock.reason }
      }
    }
    return undefined
  }

  if (toolName === "bash") {
    const command = input.command
    if (typeof command !== "string" || command.length === 0) {
      return undefined
    }
    const toolLock = findToolLock(rules, `Bash(${command})`)
    if (toolLock !== undefined) {
      return { block: true, reason: toolLock.reason }
    }
    const gitLock = findGitPushLock(rules, command)
    if (gitLock !== undefined) {
      return { block: true, reason: gitLock.reason }
    }
    return undefined
  }

  return undefined
}

function pathArgument(
  input: Record<string, unknown>
): string | undefined {
  const path = input.path
  return typeof path === "string" && path.length > 0 ? path : undefined
}

/** `git` global flags whose value is consumed and therefore not a refspec. */
const GIT_VALUE_FLAGS: ReadonlySet<string> = new Set([
  "-C",
  "-c",
  "--git-dir",
  "--work-tree",
  "--namespace",
  "--config",
])

/**
 * Extract candidate refs from a `git push ...` command. Mirrors the
 * dev-sdk hook version so the two SDKs deny the same pushes.
 *
 * Returns refs in both bare (`main`) and `refs/heads/main` forms so the
 * `git-ref` lock patterns match either spelling. Compound shell lines
 * (`cd pkg && git push origin main`) are checked segment-by-segment.
 */
export function gitPushRefCandidates(command: string): string[] {
  const candidates: string[] = []
  for (const segment of command.split(/&&|\|\||;|\||\n|\r/)) {
    collectFromSegment(segment.trim(), candidates)
  }
  return candidates
}

function collectFromSegment(command: string, out: string[]): void {
  const tokens = command.split(/\s+/)
  if (tokens[0] !== "git" && tokens[0] !== "git.exe") {
    return
  }

  let index = 1
  while (index < tokens.length && tokens[index] !== "push") {
    const token = tokens[index] ?? ""
    if (token.startsWith("-")) {
      index++
      if (GIT_VALUE_FLAGS.has(token)) {
        index++
      }
      continue
    }
    return
  }
  if (tokens[index] !== "push") {
    return
  }

  const positionals = tokens
    .slice(index + 1)
    .map(stripQuotes)
    .filter((token) => token.length > 0 && !token.startsWith("-"))
  if (positionals.length === 0) {
    return
  }

  // With two or more positionals the first is the remote; with exactly one
  // it is the refspec (the remote comes from git config).
  const refspecs = positionals.length >= 2 ? positionals.slice(1) : positionals

  for (const refspec of refspecs) {
    const destination = refspec.includes(":")
      ? (refspec.split(":").pop() ?? "")
      : refspec
    if (destination.length === 0) {
      continue
    }
    out.push(destination)
    out.push(
      destination.startsWith("refs/")
        ? destination
        : `refs/heads/${destination}`
    )
  }
}

function stripQuotes(token: string): string {
  const trimmed = token.trim()
  if (trimmed.length >= 2) {
    const first = trimmed.charAt(0)
    const last = trimmed.charAt(trimmed.length - 1)
    if ((first === '"' && last === '"') || (first === "'" && last === "'")) {
      return trimmed.slice(1, -1)
    }
  }
  return trimmed
}

function findGitPushLock(
  rules: readonly LockRule[],
  command: string
): LockRule | undefined {
  for (const candidate of gitPushRefCandidates(command)) {
    const lock = findGitRefLock(rules, candidate)
    if (lock !== undefined) {
      return lock
    }
  }
  return undefined
}

/** Re-exported for tests; not part of the public SDK surface. */
export const __testing = {
  enforceLocks,
  gitPushRefCandidates,
  PATH_TOOLS,
}

export type { PiExtensionContext }