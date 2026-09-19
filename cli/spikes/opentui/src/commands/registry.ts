/**
 * Command & keymap registry — built on `@opentui/keymap`.
 *
 * The hand-rolled `CommandRegistry` from the spike's first draft is
 * replaced with a real `Keymap` instance. This file is the *thin*
 * adapter the spike's hosts use; the actual key resolution —
 * modifiers, sequences, focus, multi-key chords — is
 * `@opentui/keymap`'s responsibility.
 *
 * What this module owns:
 *   - the canonical list of named commands (`BUILTIN_COMMANDS`)
 *   - the binding strings the spike relies on (e.g. `ctrl+p`)
 *   - the adapter surface the host uses to wire commands to its own
 *     state machine (drafts, palette visibility, approval)
 *
 * What `@opentui/keymap` owns:
 *   - keystroke parsing (modifiers, sequences, aliases)
 *   - focus and target routing
 *   - dispatch through named commands
 *
 * The split is the same one the production chat.tsx ends up making:
 * the TUI host doesn't reimplement key parsing; it asks the keymap
 * to do it. The spike is the proof that the third-party keymap
 * satisfies the issue's named-command-registry requirement end to
 * end — palette + keybindings through one surface.
 */
import type { CliRenderer, KeyEvent, Renderable } from "@opentui/core"
import type { Keymap } from "@opentui/keymap"
import { type Command, type CommandContext, type Layer } from "@opentui/keymap"
import { registerDefaultKeys } from "@opentui/keymap/addons"
import { createOpenTuiKeymap } from "@opentui/keymap/opentui"
import {
  createTestKeymap,
  type TestKeymapHarness,
  type TestKeymapEvent,
  type TestKeymapTarget,
} from "@opentui/keymap/testing"
import { tr } from "../locales/index.js"

export type SpikeCommandName =
  | "open-palette"
  | "close-palette"
  | "save-snippet"
  | "approve-plan"
  | "reject-plan"
  | "queue-followup"
  | "submit-turn"
  | "copy-last-answer"
  | "open-status"

export interface CommandSpec {
  readonly name: SpikeCommandName
  readonly label: string
  /** Default binding string used by the spike's default layer. */
  readonly key: string
  readonly description: string
}

/**
 * Static command metadata. The `name` field is a stable programming
 * handle (must NOT be translated) — the keymap dispatches by name
 * regardless of locale. The `label` and `description` are pulled
 * from the locale resource at construction time (the chat shell
 * and the keymap both consult the locale resource for the same
 * strings, so palette UI and binding help text never diverge).
 */
function cmd(
  name: SpikeCommandName,
  key: string,
  labelKey: string,
  descriptionKey: string
): CommandSpec {
  return { name, key, label: tr(labelKey), description: tr(descriptionKey) }
}

export const BUILTIN_COMMANDS: readonly CommandSpec[] = [
  cmd("open-palette", "ctrl+p", "cmd.open-palette.label", "cmd.open-palette.description"),
  cmd("close-palette", "escape", "cmd.close-palette.label", "cmd.close-palette.description"),
  cmd("save-snippet", "ctrl+s", "cmd.save-snippet.label", "cmd.save-snippet.description"),
  cmd("approve-plan", "ctrl+y", "cmd.approve-plan.label", "cmd.approve-plan.description"),
  cmd("reject-plan", "ctrl+x", "cmd.reject-plan.label", "cmd.reject-plan.description"),
  cmd("queue-followup", "alt+enter", "cmd.queue-followup.label", "cmd.queue-followup.description"),
  cmd("submit-turn", "enter", "cmd.submit-turn.label", "cmd.submit-turn.description"),
  cmd("copy-last-answer", "ctrl+shift+y", "cmd.copy-last-answer.label", "cmd.copy-last-answer.description"),
  cmd("open-status", "ctrl+o", "cmd.open-status.label", "cmd.open-status.description"),
]

/** The shape of the payload handlers receive. */
export interface CommandPayload {
  readonly text?: string
  readonly decision?: "approve" | "reject"
  readonly reason?: string
}

/**
 * The medium-risk inline approval the issue calls out. Carries the
 * five pieces the spike is supposed to prove:
 *
 *   - `intent`   — what the plan is trying to do
 *   - `scope`    — what it touches
 *   - `risk`     — `low` | `medium` | `high` (the cell colour in the card)
 *   - `planSteps` — ordered steps that will run
 *   - `diff`    — the proposed patch (one or many lines)
 *
 * The spike renders all five in the card and only the last two
 * buttons (`approve` / `reject`) dispatch a command back into the
 * keymap.
 */
export interface ApprovalPlan {
  readonly intent: string
  readonly scope: string
  readonly risk: "low" | "medium" | "high"
  readonly planSteps: readonly string[]
  readonly diff: string
}

/** Adapter the host installs: maps a command name to a handler. */
export type CommandHandler = (payload: CommandPayload) => boolean | void

/**
 * Spike-side surface. `registry.dispatch(name)` is a thin wrapper
 * over `keymap.runCommand(name, { payload })`. Tests use the
 * `createSpikeTestKeymapHarness` exported below to drive keys
 * directly through `@opentui/keymap/testing`'s host.
 */
export interface SpikeKeymap {
  /** Run a command by name, with an optional payload. */
  dispatch(name: SpikeCommandName, payload?: CommandPayload): boolean
  /**
   * Run a command by its binding alias (e.g. `"ctrl+p"`). The same
   * `Keymap` instance routes this — palette clicks and key chords
   * share one surface, so a host can wire both paths without
   * duplicating action implementations.
   */
  dispatchByKeymap(alias: string, payload?: CommandPayload): boolean
  /** List all registered commands in registration order. */
  commandNames(): readonly SpikeCommandName[]
  /** Tear down the keymap. Idempotent. */
  destroy(): void
}

type CommandWithHandler = Command<Renderable, KeyEvent, CommandPayload> & {
  __handler?: CommandHandler
}

/**
 * Build the spike's command keymap, backed by `@opentui/keymap`.
 *
 * The host passes its `CliRenderer`; the spike forwards
 * keypress events through the keymap so palette and keybindings
 * dispatch through one surface.
 */
export function createSpikeKeymap(
  renderer: CliRenderer,
  handlers: Partial<Record<SpikeCommandName, CommandHandler>> = {}
): SpikeKeymap {
  const keymap = createOpenTuiKeymap(renderer)
  // Install the default binding parser and event matcher so plain
  // strings like `"ctrl+p"` and `"alt+enter"` resolve to modifier
  // + key events. Without this, the layer registration below
  // throws `"No keymap binding parsers are registered"`.
  registerDefaultKeys(keymap)

  const commands: CommandWithHandler[] = BUILTIN_COMMANDS.map((spec) => ({
    name: spec.name,
    __handler: handlers[spec.name],
    run(ctx: CommandContext<Renderable, KeyEvent, CommandPayload>) {
      const handler = (ctx.command as CommandWithHandler | undefined)?.__handler
      if (!handler) return { ok: false as const, reason: "inactive" }
      return handler(ctx.payload ?? {})
    },
  }))

  const layer: Layer<Renderable, KeyEvent> = {
    priority: 100,
    bindings: BUILTIN_COMMANDS.map((spec) => ({
      key: spec.key,
      cmd: spec.name,
    })),
    commands,
  }

  const unregisterLayer = keymap.registerLayer(layer)

  return {
    dispatch(name, payload) {
      const result = keymap.runCommand(name, { payload })
      return result.ok
    },
    dispatchByKeymap(alias, payload) {
      const spec = BUILTIN_COMMANDS.find((s) => s.key === alias)
      if (!spec) return false
      return keymap.runCommand(spec.name, { payload }).ok
    },
    commandNames() {
      return BUILTIN_COMMANDS.map((spec) => spec.name)
    },
    destroy() {
      unregisterLayer()
    },
  }
}

/**
 * In-memory harness for tests. Returns the harness so tests can
 * drive keys through `press("ctrl+p")` etc.
 *
 * `press` accepts the same binding syntax the production keymap
 * uses: `"ctrl+p"`, `"alt+enter"`, etc. The harness parses the
 * modifier prefix and passes it as the modifier flags — the
 * underlying test host (`TestKeymapHost.press`) takes modifier
 * flags separately.
 */
export interface SpikeTestKeymapHarness {
  readonly keymap: SpikeKeymap
  readonly harness: TestKeymapHarness
  /** Press a single key. Accepts binding syntax (`"ctrl+p"`, `"alt+enter"`). */
  press(name: string, modifiers?: { shift?: boolean; ctrl?: boolean; meta?: boolean; super?: boolean }): boolean
  /** Press a multi-key chord (sequence). */
  pressSequence(names: readonly string[]): boolean
  /** Last command name dispatched — for assertions. */
  lastDispatched: { command?: SpikeCommandName; payload?: CommandPayload }
  cleanup(): void
}

function parseBindingSyntax(input: string): { name: string; modifiers: { shift?: boolean; ctrl?: boolean; meta?: boolean; super?: boolean } } {
  const parts = input.split("+").map((s) => s.trim().toLowerCase())
  const modifiers: { shift?: boolean; ctrl?: boolean; meta?: boolean; super?: boolean } = {}
  let name = parts[parts.length - 1] ?? ""
  for (const part of parts.slice(0, -1)) {
    if (part === "ctrl" || part === "control") modifiers.ctrl = true
    else if (part === "shift") modifiers.shift = true
    else if (part === "alt" || part === "meta" || part === "option") modifiers.meta = true
    else if (part === "super" || part === "cmd" || part === "command") modifiers.super = true
    else name = input // unknown modifier — keep the original
  }
  return { name, modifiers }
}

export function createSpikeTestKeymapHarness(
  handlers: Partial<Record<SpikeCommandName, CommandHandler>>
): SpikeTestKeymapHarness {
  const harness = createTestKeymap({ defaultKeys: true })
  const userHandlers = new Map<SpikeCommandName, CommandHandler>(
    Object.entries(handlers).filter(([, v]) => v).map(([k, v]) => [k as SpikeCommandName, v!])
  )
  const lastDispatched: SpikeTestKeymapHarness["lastDispatched"] = {}

  const commands = BUILTIN_COMMANDS.map<Command<TestKeymapTarget, TestKeymapEvent, CommandPayload>>(
    (spec) => ({
      name: spec.name,
      run(ctx: CommandContext<TestKeymapTarget, TestKeymapEvent, CommandPayload>) {
        lastDispatched.command = spec.name
        lastDispatched.payload = ctx.payload
        const handler = userHandlers.get(spec.name)
        if (!handler) return { ok: false as const, reason: "inactive" }
        return handler(ctx.payload ?? {})
      },
    })
  )

  const layer: Layer<TestKeymapTarget, TestKeymapEvent> = {
    priority: 100,
    bindings: BUILTIN_COMMANDS.map((spec) => ({ key: spec.key, cmd: spec.name })),
    commands,
  }
  harness.keymap.registerLayer(layer)

  const surface: SpikeKeymap = {
    dispatch(name, payload) {
      const result = harness.keymap.runCommand(name, { payload })
      return result.ok
    },
    dispatchByKeymap(alias, payload) {
      const spec = BUILTIN_COMMANDS.find((s) => s.key === alias)
      if (!spec) return false
      return harness.keymap.runCommand(spec.name, { payload }).ok
    },
    commandNames() {
      return BUILTIN_COMMANDS.map((spec) => spec.name)
    },
    destroy() {
      harness.cleanup()
    },
  }

  return {
    keymap: surface,
    harness,
    press(name, modifiers) {
      const parsed = parseBindingSyntax(name)
      const event = harness.host.press(parsed.name, { ...parsed.modifiers, ...modifiers })
      return event.name.length > 0
    },
    pressSequence(names) {
      let ok = true
      for (const key of names) {
        const parsed = parseBindingSyntax(key)
        const event = harness.host.press(parsed.name, parsed.modifiers)
        if (event.name.length === 0) ok = false
      }
      return ok
    },
    lastDispatched,
    cleanup() {
      surface.destroy()
    },
  }
}

/** Public re-exports — the spike's tests need to drive keys directly. */
export { createTestKeymap }
export type { Keymap, KeyEvent }
export type { TestKeymapHarness }