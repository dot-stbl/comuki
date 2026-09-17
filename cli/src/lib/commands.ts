/**
 * Pure command routing for `src/index.tsx`: positional args → command
 * name. Extracted from the yargs wiring so the "no args = REPL" default
 * is a testable fact, not parser behavior.
 */

export type CommandName =
  "repl" | "status" | "runs" | "login" | "whoami" | "unknown"

const KNOWN_COMMANDS: readonly CommandName[] = [
  "status",
  "runs",
  "login",
  "whoami",
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
