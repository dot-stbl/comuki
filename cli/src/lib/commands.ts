/**
 * Pure command routing for `src/index.tsx`: positional args → command
 * name. Extracted from the yargs wiring so the "no args = REPL" default
 * is a testable fact, not parser behavior.
 */

export type CommandName =
  | "repl"
  | "status"
  | "runs"
  | "login"
  | "whoami"
  | "config"
  | "setup"
  | "completion"
  | "doctor"
  | "archive"
  | "unknown"

const KNOWN_COMMANDS: readonly CommandName[] = [
  "status",
  "runs",
  "login",
  "whoami",
  "config",
  "setup",
  "completion",
  "doctor",
  "archive",
]

/**
 * Maps the positional arguments to a command. Empty args → `repl`
 * (the default: bare `comuki` opens the multi-session chat). The
 * removed `chat` subcommand is deliberately unknown — bare `comuki`
 * is the REPL now.
 */
export function resolveCommand(
  args: readonly (string | number | undefined)[]
): CommandName {
  const first = args[0]
  if (first === undefined) {
    return "repl"
  }
  const name = String(first)
  return KNOWN_COMMANDS.includes(name as CommandName)
    ? (name as CommandName)
    : "unknown"
}

/**
 * `comuki archive` action — positional subaction under `archive`. Pure
 * so the index can dispatch on a testable fact and the command file
 * stays free of yargs awareness.
 *
 *   bare `["archive"]`            → list
 *   `["archive", "list"]`         → list
 *   `["archive", "save", "s1"]`   → save with explicit sessionId
 *   `["archive", "save"]`         → save with whatever `--current` set
 *   `["archive", "bogus"]`        → unknown
 */
export type ArchiveAction =
  | { readonly kind: "list" }
  | {
      readonly kind: "save"
      readonly sessionId: string | undefined
      readonly current: boolean
    }
  | { readonly kind: "unknown"; readonly action: string }

export function resolveArchiveAction(
  args: readonly (string | number | undefined)[],
  current: boolean
): ArchiveAction {
  const action = args[1]
  if (action === undefined) {
    return { kind: "list" }
  }
  const name = String(action)
  if (name === "list") {
    return { kind: "list" }
  }
  if (name === "save") {
    return {
      kind: "save",
      sessionId: args[2] !== undefined ? String(args[2]) : undefined,
      current,
    }
  }
  return { kind: "unknown", action: name }
}
