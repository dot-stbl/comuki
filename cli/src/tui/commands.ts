/**
 * Named-command registry for the OpenTUI host — built on
 * `@opentui/keymap` (the official `@opentui/keymap/opentui` adapter).
 *
 * Adapted from the ADR-0002 spike's `commands/registry.ts` with the
 * production command set. One registry drives everything: palette
 * dispatch and key chords resolve through the same
 * `keymap.runCommand(name)` — there is no parallel handler map.
 *
 * Two layers:
 * - the **base layer** (always registered): submit-turn, cancel-turn,
 *   new-session, close-session, exit;
 * - the **approval layer** (registered by the host only while an
 *   approval card is up): plain `y` / `n` — so typing `y` into the
 *   composer is unaffected while no card is showing.
 *
 * Command names are stable programming handles — never translated.
 */

import type { CliRenderer, KeyEvent, Renderable } from "@opentui/core"
import {
  type Command,
  type CommandContext,
  type Keymap,
  type Layer,
} from "@opentui/keymap"
import { registerDefaultKeys } from "@opentui/keymap/addons"
import { createOpenTuiKeymap } from "@opentui/keymap/opentui"
import {
  createTestKeymap,
  type TestKeymapHarness,
  type TestKeymapEvent,
  type TestKeymapTarget,
} from "@opentui/keymap/testing"
import { tr, type I18nInstance } from "../locales"

export type TuiCommandName =
  | "submit-turn"
  | "cancel-turn"
  | "approve"
  | "reject"
  | "new-session"
  | "close-session"
  | "exit"
  | "help"
  | "rename-session"
  | "clear-draft"
  | "open-editor"
  | "open-palette"

/**
 * Availability tags every command declares ONCE. The slash menu, the
 * palette and the help view all filter on the same tag against the
 * live `TuiCommandContext`; key dispatch additionally guards in the
 * handler (the tag is for listing, the handler is the enforcement).
 */
export type TuiAvailability =
  | "always"
  | "session"
  | "draft"
  | "thinking"
  | "approval"

/** The live facts availability is judged against. */
export interface TuiCommandContext {
  readonly sessionOpen: boolean
  readonly hasDraft: boolean
  readonly turnKind: "idle" | "thinking" | "awaiting-approval" | "failed"
}

/** The `/`-surface of a command — present when the command is slash-reachable. */
export interface TuiSlashSpec {
  /** Canonical slash token (`/exit`). */
  readonly name: string
  /** Extra tokens resolving to the same command (`/quit`, `/q`). */
  readonly aliases: readonly string[]
  /** i18n key for the argument hint shown in the menu (`<title>`). */
  readonly argsHintKey: string | null
}

export interface TuiCommandSpec {
  readonly name: TuiCommandName
  /**
   * Default binding string used by the base layer; `""` means the
   * command has no key chord (slash/palette only).
   */
  readonly key: string
  readonly label: string
  readonly description: string
  readonly slash: TuiSlashSpec | null
  readonly available: TuiAvailability
}

export function commandAvailable(
  spec: TuiCommandSpec,
  context: TuiCommandContext
): boolean {
  switch (spec.available) {
    case "always":
      return true
    case "session":
      return context.sessionOpen
    case "draft":
      return context.hasDraft
    case "thinking":
      return context.turnKind === "thinking"
    case "approval":
      return context.turnKind === "awaiting-approval"
  }
}

/** Every command the registry knows — base + approval, one list. */
export function allTuiCommands(i18n: I18nInstance): readonly TuiCommandSpec[] {
  return [...buildTuiCommands(i18n), ...buildApprovalCommands(i18n)]
}

/**
 * Build the canonical command list from the locale resource. Called
 * once at host creation; labels/descriptions come from i18next, the
 * `name`/`key` fields are locale-independent.
 *
 * Note: OpenTUI names the physical Enter key `return` (keymaps
 * display it as "enter"); `kpenter` is the numpad Enter. The layer
 * binds both to submit-turn.
 */
export function buildTuiCommands(i18n: I18nInstance): readonly TuiCommandSpec[] {
  return [
    command(i18n, "submit-turn", "return", "draft", null),
    command(i18n, "cancel-turn", "escape", "thinking", {
      name: "stop",
      aliases: [],
      argsHintKey: null,
    }),
    command(i18n, "new-session", "ctrl+n", "always", {
      name: "new",
      aliases: [],
      argsHintKey: null,
    }),
    command(i18n, "close-session", "ctrl+w", "session", {
      name: "close",
      aliases: [],
      argsHintKey: null,
    }),
    command(i18n, "exit", "ctrl+c", "always", {
      name: "exit",
      aliases: ["quit", "q"],
      argsHintKey: null,
    }),
    command(i18n, "help", "", "always", {
      name: "help",
      aliases: [],
      argsHintKey: null,
    }),
    command(i18n, "rename-session", "", "session", {
      name: "rename",
      aliases: [],
      argsHintKey: "cmd.rename-session.argsHint",
    }),
    command(i18n, "clear-draft", "", "draft", {
      name: "clear-draft",
      aliases: [],
      argsHintKey: null,
    }),
    command(i18n, "open-editor", "ctrl+e", "always", null),
    command(i18n, "open-palette", "ctrl+p", "always", null),
  ]
}

/** The approval-only commands — bound to plain y/n in their own layer. */
export function buildApprovalCommands(i18n: I18nInstance): readonly TuiCommandSpec[] {
  return [
    command(i18n, "approve", "y", "approval", {
      name: "approve",
      aliases: [],
      argsHintKey: null,
    }),
    command(i18n, "reject", "n", "approval", {
      name: "reject",
      aliases: [],
      argsHintKey: null,
    }),
  ]
}

function command(
  i18n: I18nInstance,
  name: TuiCommandName,
  key: string,
  available: TuiAvailability,
  slash: TuiSlashSpec | null
): TuiCommandSpec {
  return {
    name,
    key,
    label: tr(i18n, `cmd.${name}.label`),
    description: tr(i18n, `cmd.${name}.description`),
    slash,
    available,
  }
}

/** The shape of the payload handlers receive. */
export interface TuiCommandPayload {
  readonly text?: string
  readonly reason?: string
}

export type TuiCommandHandler = (payload: TuiCommandPayload) => boolean | void

/** Adapter surface the host installs: command name → handler. */
export type TuiHandlers = Partial<Record<TuiCommandName, TuiCommandHandler>>

/**
 * A transient UI-layer binding (menu/palette navigation). Not a named
 * command — these are surface-local actions the host registers while
 * an overlay owns the arrow/enter keys.
 */
export interface TuiUiBinding {
  readonly key: string
  readonly run: () => boolean | void
}

export interface TuiKeymapSurface {
  /** Run a command by name, with an optional payload. */
  dispatch(name: TuiCommandName, payload?: TuiCommandPayload): boolean
  /** Run a command by its binding alias (same registry, same layer). */
  dispatchByKeymap(alias: string, payload?: TuiCommandPayload): boolean
  commandNames(): readonly TuiCommandName[]
  /**
   * Register the approval layer (y/n). Returns the unregister
   * function; the host calls it when the card clears so plain
   * typing is untouched while no card is up.
   */
  registerApprovalLayer(handlers: TuiHandlers): () => void
  /**
   * Register a transient UI layer (priority defaults above every
   * named-command layer) — the slash menu / palette / help view own
   * their keys while open. Returns the unregister function.
   */
  registerUiLayer(
    bindings: readonly TuiUiBinding[],
    priority?: number
  ): () => void
  destroy(): void
}

/** UI layers sit above the approval layer (200) and the base (100). */
export const TUI_UI_LAYER_PRIORITY = 300

type CommandWithHandler = Command<Renderable, KeyEvent, TuiCommandPayload> & {
  __handler?: TuiCommandHandler
}

function commandsWithHandlers(
  specs: readonly TuiCommandSpec[],
  handlers: TuiHandlers
): readonly CommandWithHandler[] {
  return specs.map((spec) => ({
    name: spec.name,
    __handler: handlers[spec.name],
    run(ctx: CommandContext<Renderable, KeyEvent, TuiCommandPayload>) {
      const handler = (ctx.command as CommandWithHandler | undefined)?.__handler
      if (!handler) {
        return { ok: false as const, reason: "inactive" }
      }
      return handler(ctx.payload ?? {})
    },
  }))
}

function layerFor(
  specs: readonly TuiCommandSpec[],
  commands: readonly CommandWithHandler[],
  priority: number
): Layer<Renderable, KeyEvent> {
  return {
    priority,
    bindings: specs.flatMap<{ key: string; cmd: string }>((spec) =>
      spec.key.length === 0
        ? []
        : spec.name === "submit-turn"
          ? [
              { key: spec.key, cmd: spec.name },
              { key: "kpenter", cmd: spec.name },
            ]
          : [{ key: spec.key, cmd: spec.name }]
    ),
    commands,
  }
}

function uiLayerFor(
  bindings: readonly TuiUiBinding[],
  priority: number
): Layer<Renderable, KeyEvent> {
  return {
    priority,
    bindings: bindings.map((binding) => ({
      key: binding.key,
      cmd: () => binding.run() !== false,
    })),
  }
}

/**
 * Build the host's command keymap over a real renderer. The surface
 * mirrors the spike's: `dispatch` / `dispatchByKeymap` /
 * `commandNames` / `destroy`, plus approval-layer registration.
 */
export function createTuiKeymap(
  renderer: CliRenderer,
  i18n: I18nInstance,
  handlers: TuiHandlers
): TuiKeymapSurface {
  const commands = buildTuiCommands(i18n)
  const commandByKey = new Map(
    commands
      .filter((spec) => spec.key.length > 0)
      .map((spec) => [spec.key, spec])
  )

  const keymap: Keymap<Renderable, KeyEvent> = createOpenTuiKeymap(renderer)
  registerDefaultKeys(keymap)

  const unregisterBase = keymap.registerLayer(
    layerFor(commands, commandsWithHandlers(commands, handlers), 100)
  )

  function registerApprovalLayer(approvalHandlers: TuiHandlers): () => void {
    const specs = buildApprovalCommands(i18n)
    return keymap.registerLayer(
      layerFor(specs, commandsWithHandlers(specs, approvalHandlers), 200)
    )
  }

  function registerUiLayer(
    bindings: readonly TuiUiBinding[],
    priority: number = TUI_UI_LAYER_PRIORITY
  ): () => void {
    return keymap.registerLayer(uiLayerFor(bindings, priority))
  }

  function runBySpec(
    spec: TuiCommandSpec | undefined,
    payload: TuiCommandPayload | undefined
  ): boolean {
    if (!spec) {
      return false
    }
    return keymap.runCommand(spec.name, { payload }).ok
  }

  return {
    dispatch(name, payload) {
      return keymap.runCommand(name, { payload }).ok
    },
    dispatchByKeymap(alias, payload) {
      return runBySpec(commandByKey.get(alias), payload)
    },
    commandNames() {
      return commands.map((spec) => spec.name)
    },
    registerApprovalLayer,
    registerUiLayer,
    destroy() {
      unregisterBase()
      commandByKey.clear()
    },
  }
}

/**
 * Test-only harness over `@opentui/keymap/testing` — no real
 * `CliRenderer` required. Mirrors the spike harness so key semantics
 * (exactly-once dispatch, unbound keys, approval gating) are testable
 * in isolation.
 */
export interface TuiTestKeymapHarness {
  readonly keymap: TuiKeymapSurface
  readonly harness: TestKeymapHarness
  press(name: string): boolean
  lastDispatched: { command?: TuiCommandName; payload?: TuiCommandPayload }
  cleanup(): void
}

function parseBindingSyntax(input: string): {
  name: string
  modifiers: { shift?: boolean; ctrl?: boolean; meta?: boolean; super?: boolean }
} {
  const parts = input.split("+").map((part) => part.trim().toLowerCase())
  const modifiers: {
    shift?: boolean
    ctrl?: boolean
    meta?: boolean
    super?: boolean
  } = {}
  let name = parts[parts.length - 1] ?? ""
  for (const part of parts.slice(0, -1)) {
    if (part === "ctrl" || part === "control") {
      modifiers.ctrl = true
    } else if (part === "shift") {
      modifiers.shift = true
    } else if (part === "alt" || part === "meta" || part === "option") {
      modifiers.meta = true
    } else if (part === "super" || part === "cmd" || part === "command") {
      modifiers.super = true
    } else {
      name = input
    }
  }
  return { name, modifiers }
}

export function createTuiTestKeymapHarness(
  handlers: TuiHandlers,
  i18n: I18nInstance
): TuiTestKeymapHarness {
  const commands = buildTuiCommands(i18n)
  const harness = createTestKeymap({ defaultKeys: true })
  const lastDispatched: TuiTestKeymapHarness["lastDispatched"] = {}

  function localCommands(
    specs: readonly TuiCommandSpec[],
    userHandlers: TuiHandlers
  ): readonly Command<TestKeymapTarget, TestKeymapEvent, TuiCommandPayload>[] {
    return specs.map((spec) => ({
      name: spec.name,
      run(ctx: CommandContext<TestKeymapTarget, TestKeymapEvent, TuiCommandPayload>) {
        lastDispatched.command = spec.name
        lastDispatched.payload = ctx.payload
        const handler = userHandlers[spec.name]
        if (!handler) {
          return { ok: false as const, reason: "inactive" }
        }
        return handler(ctx.payload ?? {})
      },
    }))
  }

  harness.keymap.registerLayer({
    priority: 100,
    bindings: commands
      .filter((spec) => spec.key.length > 0)
      .map((spec) => ({ key: spec.key, cmd: spec.name })),
    commands: localCommands(commands, handlers),
  })

  const commandByKey = new Map(
    commands
      .filter((spec) => spec.key.length > 0)
      .map((spec) => [spec.key, spec])
  )

  const surface: TuiKeymapSurface = {
    dispatch(name, payload) {
      return harness.keymap.runCommand(name, { payload }).ok
    },
    dispatchByKeymap(alias, payload) {
      const spec = commandByKey.get(alias)
      if (!spec) {
        return false
      }
      return harness.keymap.runCommand(spec.name, { payload }).ok
    },
    commandNames() {
      return commands.map((spec) => spec.name)
    },
    registerApprovalLayer(approvalHandlers) {
      const specs = buildApprovalCommands(i18n)
      return harness.keymap.registerLayer({
        priority: 200,
        bindings: specs
          .filter((spec) => spec.key.length > 0)
          .map((spec) => ({ key: spec.key, cmd: spec.name })),
        commands: localCommands(specs, approvalHandlers),
      })
    },
    registerUiLayer(bindings, priority = TUI_UI_LAYER_PRIORITY) {
      return harness.keymap.registerLayer({
        priority,
        bindings: bindings.map((binding) => ({
          key: binding.key,
          cmd: () => binding.run() !== false,
        })),
        commands: [],
      })
    },
    destroy() {
      harness.cleanup()
    },
  }

  return {
    keymap: surface,
    harness,
    press(name) {
      const parsed = parseBindingSyntax(name)
      const event = harness.host.press(parsed.name, parsed.modifiers)
      return event.name.length > 0
    },
    lastDispatched,
    cleanup() {
      surface.destroy()
    },
  }
}

// ---------------------------------------------------------------------------
// Slash-menu derivations — pure functions over the registry (the Ink
// composer's `lib/slash` behavior, ported onto the named-command
// registry; tui/ never imports ../lib).
// ---------------------------------------------------------------------------

/**
 * The autocomplete filter query for the current composer text, or
 * `null` when the menu must not show.
 *
 * The menu lives only while the whole draft is one partial command: a
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
 * Slash-reachable commands whose name or an alias starts with
 * `query` (case-insensitive) AND are available in `context`, in
 * registry order. An empty query matches everything available.
 */
export function filterSlashCommands(
  specs: readonly TuiCommandSpec[],
  query: string,
  context: TuiCommandContext
): readonly TuiCommandSpec[] {
  const lowered = query.toLowerCase()
  return specs.filter(
    (spec) =>
      spec.slash !== null &&
      commandAvailable(spec, context) &&
      (spec.slash.name.startsWith(lowered) ||
        spec.slash.aliases.some((alias) => alias.startsWith(lowered)))
  )
}

/**
 * The completed composer text for a menu selection: the canonical
 * `/<name>` plus one trailing space (ready for arguments; the space
 * itself closes the menu — `slashMenuQuery` sees whitespace).
 */
export function completeSlashCommand(spec: TuiCommandSpec): string {
  const name = spec.slash?.name ?? ""
  return `/${name} `
}

/**
 * Parse a submitted draft as a slash command. Returns the matched
 * spec plus the raw argument text, or `null` when the input is not a
 * registered command (it goes to the brain as a chat message — same
 * rule as the Ink host's `resolveSlashAction`).
 */
export function parseSlashInput(
  raw: string,
  specs: readonly TuiCommandSpec[]
): { readonly spec: TuiCommandSpec; readonly args: string } | null {
  if (!raw.startsWith("/")) {
    return null
  }
  const bare = raw.slice(1).trim()
  const [head, ...rest] = bare.split(/\s+/)
  const token = (head ?? "").toLowerCase()
  if (token.length === 0) {
    return null
  }
  const args = rest.join(" ").trim()
  const spec = specs.find(
    (candidate) =>
      candidate.slash !== null &&
      (candidate.slash.name === token ||
        candidate.slash.aliases.includes(token))
  )
  return spec === undefined ? null : { spec, args }
}

// ---------------------------------------------------------------------------
// Palette derivations — tiered fuzzy filter over the same registry.
// ---------------------------------------------------------------------------

/**
 * Fuzzy tier for `query` against `target` (both lowercased inside):
 * `0` prefix match, `1` word-start subsequence, `2` plain
 * subsequence, `null` no match. Case-insensitive.
 */
export function fuzzyTier(query: string, target: string): number | null {
  if (query.length === 0) {
    return 0
  }
  const q = query.toLowerCase()
  const t = target.toLowerCase()
  if (t.startsWith(q)) {
    return 0
  }
  let tier = 2
  let searchFrom = 0
  for (const char of q) {
    const index = t.indexOf(char, searchFrom)
    if (index < 0) {
      return null
    }
    const previous = index > 0 ? t[index - 1] : ""
    if (index === 0 || /[\s\-/]/.test(previous)) {
      tier = 1
    }
    searchFrom = index + 1
  }
  return tier
}

/**
 * Palette entries for `query`: every available command whose
 * `name label` fuzzy-matches, tiered (prefix < word-start <
 * subsequence) and stable in registry order inside a tier. An empty
 * query lists everything available.
 */
export function paletteMatches(
  specs: readonly TuiCommandSpec[],
  query: string,
  context: TuiCommandContext
): readonly TuiCommandSpec[] {
  const available = specs.filter((spec) => commandAvailable(spec, context))
  if (query.length === 0) {
    return available
  }
  const tiered: TuiCommandSpec[][] = [[], [], []]
  for (const spec of available) {
    const tier = fuzzyTier(query, `${spec.name} ${spec.label}`)
    if (tier !== null) {
      tiered[tier]?.push(spec)
    }
  }
  return [...tiered[0], ...tiered[1], ...tiered[2]]
}
