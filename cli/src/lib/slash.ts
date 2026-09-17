/**
 * Slash-command surface of the chat REPL: one registry, one parser.
 *
 * `commands.ts` routes argv subcommands (`comuki status`); this module
 * routes in-REPL input (`/retry`, `rename my tab`, …). The registry is
 * the single source of truth — `/help` renders from it, so a new
 * command appears in the list the moment it is registered.
 */
import { colors } from "../theme"

export interface SlashCommand {
  /** Canonical token matched after stripping the leading `/`. */
  readonly name: string
  /** Extra tokens that resolve to this command (`quit`, `q` → `exit`). */
  readonly aliases: readonly string[]
  /** Label shown in `/help` — includes the slash and argument hints. */
  readonly usage: string
  readonly description: string
}

export const SLASH_COMMANDS: readonly SlashCommand[] = [
  {
    name: "exit",
    aliases: ["quit", "q"],
    usage: "/exit, /quit, /q",
    description: "leave the cli",
  },
  {
    name: "retry",
    aliases: [],
    usage: "/retry",
    description: "resend the last message",
  },
  {
    name: "rename",
    aliases: [],
    usage: "/rename <title>",
    description: "rename the active session",
  },
  {
    name: "export",
    aliases: [],
    usage: "/export [path]",
    description: "save the transcript as markdown",
  },
  {
    name: "bell",
    aliases: [],
    usage: "/bell on|off",
    description: "bell + toast when a turn completes",
  },
  {
    name: "clear",
    aliases: [],
    usage: "/clear",
    description: "wipe the active transcript",
  },
  {
    name: "help",
    aliases: [],
    usage: "/help",
    description: "this list",
  },
  {
    name: "sessions",
    aliases: [],
    usage: "/sessions",
    description: "session overview (esc)",
  },
  {
    name: "new",
    aliases: [],
    usage: "/new",
    description: "new session (ctrl+n)",
  },
  {
    name: "approve",
    aliases: [],
    usage: "approve",
    description: "release a pending plan",
  },
  {
    name: "reject",
    aliases: [],
    usage: "reject [reason]",
    description: "decline a pending plan",
  },
]

/** The `/help` transcript block, rendered from the registry. */
export function slashHelpLines(): readonly string[] {
  const width = Math.max(
    ...SLASH_COMMANDS.map((command) => command.usage.length)
  )
  return [
    `${colors.accent}commands${colors.reset}`,
    ...SLASH_COMMANDS.map(
      (command) => `  ${command.usage.padEnd(width + 3)}${command.description}`
    ),
  ]
}

export type SlashAction =
  | { readonly kind: "exit" }
  | { readonly kind: "retry" }
  | { readonly kind: "rename"; readonly title: string }
  | { readonly kind: "export"; readonly path?: string }
  | { readonly kind: "bell"; readonly enabled?: boolean }
  | { readonly kind: "clear" }
  | { readonly kind: "help" }
  | { readonly kind: "sessions" }
  | { readonly kind: "new" }
  | { readonly kind: "approve" }
  | { readonly kind: "reject"; readonly reason?: string }
  /** Not a command — the input goes to the brain as a chat message. */
  | { readonly kind: "message" }

/**
 * Maps one submitted input line to an action. Pure: session state (what
 * `/retry` finds, whether `/rename` has a target) is resolved by the
 * caller, so the full command grammar is testable without Ink.
 */
export function resolveSlashAction(raw: string): SlashAction {
  const bare = raw.replace(/^\//, "").trim()
  const [head, ...rest] = bare.split(/\s+/)
  const name = (head ?? "").toLowerCase()
  const args = rest.join(" ").trim()

  if (matches("exit", name)) {
    return { kind: "exit" }
  }
  if (matches("retry", name)) {
    return { kind: "retry" }
  }
  if (matches("rename", name)) {
    return { kind: "rename", title: args.replace(/\s+/g, " ") }
  }
  if (matches("export", name)) {
    return args.length === 0 ? { kind: "export" } : { kind: "export", path: args }
  }
  if (matches("bell", name)) {
    if (args === "on") {
      return { kind: "bell", enabled: true }
    }
    if (args === "off") {
      return { kind: "bell", enabled: false }
    }
    // Bare `/bell` (or anything unparsable) reads as a status query.
    return { kind: "bell" }
  }
  if (matches("clear", name)) {
    return { kind: "clear" }
  }
  if (matches("help", name)) {
    return { kind: "help" }
  }
  if (matches("sessions", name)) {
    return { kind: "sessions" }
  }
  if (matches("new", name)) {
    return { kind: "new" }
  }
  if (matches("approve", name)) {
    return { kind: "approve" }
  }
  if (matches("reject", name)) {
    return args.length === 0
      ? { kind: "reject" }
      : { kind: "reject", reason: args }
  }
  return { kind: "message" }
}

function matches(command: string, name: string): boolean {
  const entry = SLASH_COMMANDS.find(
    (candidate) => candidate.name === command
  )
  return (
    entry !== undefined && (name === entry.name || entry.aliases.includes(name))
  )
}
