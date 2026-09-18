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
    name: "edit",
    aliases: [],
    usage: "/edit",
    description: "put the last message back in the prompt",
  },
  {
    name: "copycode",
    aliases: [],
    usage: "/copycode",
    description: "copy the last fenced code block",
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
    name: "stop",
    aliases: [],
    usage: "/stop",
    description: "abort the running turn",
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
  {
    name: "runs",
    aliases: [],
    usage: "/runs",
    description: "live runs feed panel",
  },
  {
    name: "workers",
    aliases: [],
    usage: "/workers",
    description: "background workers status",
  },
  {
    name: "plan",
    aliases: [],
    usage: "/plan",
    description: "this session's plan",
  },
  {
    name: "project",
    aliases: [],
    usage: "/project <id|slug|name>",
    description: "switch project context",
  },
  {
    name: "snip",
    aliases: [],
    usage: "/snip [name|save <name>|rm <name>]",
    description: "saved prompts — /snip <name> sends one",
  },
  {
    name: "branch",
    aliases: [],
    usage: "/branch [message]",
    description: "fork this session into a new tab",
  },
  {
    name: "kb",
    aliases: [],
    usage: "/kb add <file|glob> | /kb list",
    description: "knowledge library — ingest files, list documents",
  },
  {
    name: "theme",
    aliases: [],
    usage: "/theme [name]",
    description: "list palettes or switch live",
  },
  {
    name: "whoami",
    aliases: [],
    usage: "/whoami",
    description: "identity, roles, permissions",
  },
  {
    name: "keys",
    aliases: [],
    usage: "/keys",
    description: "current keybindings",
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
  | { readonly kind: "edit" }
  | { readonly kind: "copycode" }
  | { readonly kind: "rename"; readonly title: string }
  | { readonly kind: "export"; readonly path?: string }
  | { readonly kind: "bell"; readonly enabled?: boolean }
  | { readonly kind: "clear" }
  | { readonly kind: "stop" }
  | { readonly kind: "help" }
  | { readonly kind: "sessions" }
  | { readonly kind: "new" }
  | { readonly kind: "approve" }
  | { readonly kind: "reject"; readonly reason?: string }
  | { readonly kind: "runs" }
  | { readonly kind: "workers" }
  | { readonly kind: "plan" }
  | { readonly kind: "project"; readonly query: string }
  | { readonly kind: "snip" }
  | { readonly kind: "snip-send"; readonly name: string }
  | { readonly kind: "snip-save"; readonly name: string }
  | { readonly kind: "snip-rm"; readonly name: string }
  | { readonly kind: "branch"; readonly message?: string }
  /** `/kb add notes.md` → subcommand `add`, rest `notes.md`; bare `/kb` → both empty. */
  | { readonly kind: "kb"; readonly subcommand: string; readonly rest: string }
  | { readonly kind: "theme"; readonly name: string }
  | { readonly kind: "whoami" }
  | { readonly kind: "keys" }
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
  if (matches("edit", name)) {
    return { kind: "edit" }
  }
  if (matches("copycode", name)) {
    return { kind: "copycode" }
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
  if (matches("stop", name)) {
    return { kind: "stop" }
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
  if (matches("runs", name)) {
    return { kind: "runs" }
  }
  if (matches("workers", name)) {
    return { kind: "workers" }
  }
  if (matches("plan", name)) {
    return { kind: "plan" }
  }
  if (matches("project", name)) {
    return { kind: "project", query: args }
  }
  if (matches("snip", name)) {
    // `save` / `rm` / `list` as the first argument token are reserved
    // subcommand words — snippet names matching them are unreachable.
    const [sub, ...rest] = args.split(/\s+/)
    const target = rest.join(" ").trim()
    if (args.length === 0 || sub === "list") {
      return { kind: "snip" }
    }
    if (sub === "save") {
      return { kind: "snip-save", name: target }
    }
    if (sub === "rm") {
      return { kind: "snip-rm", name: target }
    }
    return { kind: "snip-send", name: args }
  }
  if (matches("branch", name)) {
    return args.length === 0
      ? { kind: "branch" }
      : { kind: "branch", message: args }
  }
  if (matches("kb", name)) {
    const [sub, ...subRest] = args.split(/\s+/).filter((token) => token.length > 0)
    return {
      kind: "kb",
      subcommand: (sub ?? "").toLowerCase(),
      rest: subRest.join(" ").trim(),
    }
  }
  if (matches("theme", name)) {
    return { kind: "theme", name: args.toLowerCase() }
  }
  if (matches("whoami", name)) {
    return { kind: "whoami" }
  }
  if (matches("keys", name)) {
    return { kind: "keys" }
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

// ---------------------------------------------------------------------------
// Inline autocomplete menu — pure derivations over the registry
// ---------------------------------------------------------------------------

/**
 * The autocomplete filter query for the current prompt value, or null
 * when the menu must not show.
 *
 * The menu lives only while the whole line is one partial command: a
 * leading `/`, no whitespace yet (a space starts arguments → close).
 * `/` alone shows the full list; the query is lowercased for matching.
 */
export function slashMenuQuery(value: string): string | null {
  if (!value.startsWith("/")) {
    return null
  }
  const rest = value.slice(1)
  if (/\s/.test(rest)) {
    return null
  }
  return rest.toLowerCase()
}

/**
 * Commands whose name or an alias starts with `query` (case-insensitive),
 * in registry order. An empty query matches everything.
 */
export function filterSlashCommands(
  commands: readonly SlashCommand[],
  query: string
): readonly SlashCommand[] {
  const lowered = query.toLowerCase()
  return commands.filter(
    (command) =>
      command.name.startsWith(lowered) ||
      command.aliases.some((alias) => alias.startsWith(lowered))
  )
}

/**
 * The completed prompt value for a menu selection: the canonical
 * `/name` plus one trailing space (ready for arguments, and the space
 * itself closes the menu — `slashMenuQuery` sees whitespace).
 */
export function completeSlashCommand(
  command: SlashCommand
): string {
  return `/${command.name} `
}
