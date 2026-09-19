/**
 * Command & keymap registry — built on `@opentui/keymap`.
 *
 * The spike uses a real `Keymap` instance. This file is the *thin*
 * adapter the spike's hosts use; the actual key resolution —
 * modifiers, sequences, focus, multi-key chords — is
 * `@opentui/keymap`'s responsibility.
 *
 * What this module owns:
 *   - the canonical list of named commands (`buildBuiltinCommands`)
 *   - the binding strings the spike relies on (e.g. `ctrl+p`)
 *   - the adapter surface the host uses to wire commands to its own
 *     state machine (drafts, palette visibility, approval)
 *
 * What `@opentui/keymap` owns:
 *   - keystroke parsing (modifiers, sequences, aliases)
 *   - focus and target routing
 *   - dispatch through named commands
 */
import type { CliRenderer, KeyEvent, Renderable } from "@opentui/core"
import { type Command, type CommandContext, type Keymap, type Layer } from "@opentui/keymap"
import { registerDefaultKeys } from "@opentui/keymap/addons"
import { createOpenTuiKeymap } from "@opentui/keymap/opentui"
import {
  createTestKeymap,
  type TestKeymapHarness,
  type TestKeymapEvent,
  type TestKeymapTarget,
} from "@opentui/keymap/testing"
import { tr, type I18nInstance } from "../locales/index.js"

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
 * from the i18next resource at construction time.
 *
 * `buildBuiltinCommands(i18n)` is called once at host creation; the
 * spike-local tests can pass a fresh `createI18nFor("ru")` to assert
 * the ru labels flow through to the keymap and the chat shell.
 */
function buildCmd(
  i18n: I18nInstance,
  name: SpikeCommandName,
  key: string,
  labelKey: string,
  descriptionKey: string,
): CommandSpec {
  return {
    name,
    key,
    label: tr(i18n, labelKey),
    description: tr(i18n, descriptionKey),
  }
}

export function buildBuiltinCommands(i18n: I18nInstance): readonly CommandSpec[] {
  return [
    buildCmd(i18n, "open-palette", "ctrl+p", "cmd.open-palette.label", "cmd.open-palette.description"),
    buildCmd(i18n, "close-palette", "escape", "cmd.close-palette.label", "cmd.close-palette.description"),
    buildCmd(i18n, "save-snippet", "ctrl+s", "cmd.save-snippet.label", "cmd.save-snippet.description"),
    buildCmd(i18n, "approve-plan", "ctrl+y", "cmd.approve-plan.label", "cmd.approve-plan.description"),
    buildCmd(i18n, "reject-plan", "ctrl+x", "cmd.reject-plan.label", "cmd.reject-plan.description"),
    buildCmd(i18n, "queue-followup", "alt+enter", "cmd.queue-followup.label", "cmd.queue-followup.description"),
    buildCmd(i18n, "submit-turn", "enter", "cmd.submit-turn.label", "cmd.submit-turn.description"),
    buildCmd(i18n, "copy-last-answer", "ctrl+shift+y", "cmd.copy-last-answer.label", "cmd.copy-last-answer.description"),
    buildCmd(i18n, "open-status", "ctrl+o", "cmd.open-status.label", "cmd.open-status.description"),
  ]
}

/** The shape of the payload handlers receive. */
export interface CommandPayload {
  readonly text?: string
  readonly decision?: "approve" | "reject"
  readonly reason?: string
}

/**
 * The medium-risk inline approval the issue calls out.
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
 * Spike-side surface. `dispatch(name)` is a thin wrapper over
 * `keymap.runCommand(name, { payload })`.
 */
export interface SpikeKeymap {
  /** Run a command by name, with an optional payload. */
  dispatch(name: SpikeCommandName, payload?: CommandPayload): boolean
  /** Run a command by its binding alias. */
  dispatchByKeymap(alias: string, payload?: CommandPayload): boolean
  commandNames(): readonly SpikeCommandName[]
  destroy(): void
}

type CommandWithHandler = Command<Renderable, KeyEvent, CommandPayload> & {
  __handler?: CommandHandler
}

/**
 * Build the spike's command keymap, backed by `@opentui/keymap`.
 *
 * `i18n` is required: the spike reads labels / descriptions from the
 * i18next resource to populate the command layer once at host
 * creation. Tests pass a fresh `await createI18nFor("ru")` instance to
 * assert the ru locale flows through.
 */
export function createSpikeKeymap(
  renderer: CliRenderer,
  i18n: I18nInstance,
  handlers: Partial<Record<SpikeCommandName, CommandHandler>> = {},
): SpikeKeymap {
  const commands = buildBuiltinCommands(i18n)
  const commandByKey = new Map<string, CommandSpec>(
    commands.map((c) => [c.key, c]),
  )

  const keymap = createOpenTuiKeymap(renderer)
  registerDefaultKeys(keymap)

  const commandLayer: Layer<Renderable, KeyEvent> = {
    priority: 100,
    bindings: commands.map<{ key: string; cmd: string }>((c) => ({
      key: c.key,
      cmd: c.name,
    })),
    commands: commands.map<CommandWithHandler>((spec) => ({
      name: spec.name,
      __handler: handlers[spec.name],
      run(ctx: CommandContext<Renderable, KeyEvent, CommandPayload>) {
        const handler = (ctx.command as CommandWithHandler | undefined)?.__handler
        if (!handler) return { ok: false as const, reason: "inactive" }
        return handler(ctx.payload ?? {})
      },
    })),
  }
  const unregisterLayer = keymap.registerLayer(commandLayer)

  return {
    dispatch(name, payload) {
      const result = keymap.runCommand(name, { payload })
      return result.ok
    },
    dispatchByKeymap(alias, payload) {
      const spec = commandByKey.get(alias)
      if (!spec) return false
      return keymap.runCommand(spec.name, { payload }).ok
    },
    commandNames() {
      return commands.map((c) => c.name)
    },
    destroy() {
      unregisterLayer()
      commandByKey.clear()
    },
  }
}

/**
 * In-memory harness for tests. Returns the harness so tests can
 * drive keys through `press("ctrl+p")` etc.
 */
export interface SpikeTestKeymapHarness {
  readonly keymap: SpikeKeymap
  readonly harness: TestKeymapHarness
  press(
    name: string,
    modifiers?: { shift?: boolean; ctrl?: boolean; meta?: boolean; super?: boolean },
  ): boolean
  pressSequence(names: readonly string[]): boolean
  lastDispatched: { command?: SpikeCommandName; payload?: CommandPayload }
  cleanup(): void
}

function parseBindingSyntax(input: string): {
  name: string
  modifiers: { shift?: boolean; ctrl?: boolean; meta?: boolean; super?: boolean }
} {
  const parts = input.split("+").map((s) => s.trim().toLowerCase())
  const modifiers: { shift?: boolean; ctrl?: boolean; meta?: boolean; super?: boolean } = {}
  let name = parts[parts.length - 1] ?? ""
  for (const part of parts.slice(0, -1)) {
    if (part === "ctrl" || part === "control") modifiers.ctrl = true
    else if (part === "shift") modifiers.shift = true
    else if (part === "alt" || part === "meta" || part === "option") modifiers.meta = true
    else if (part === "super" || part === "cmd" || part === "command") modifiers.super = true
    else name = input
  }
  return { name, modifiers }
}

/**
 * Test-only harness. Uses the test keymap host from
 * `@opentui/keymap/testing` — no real `CliRenderer` required.
 * The i18n instance is required: command labels and descriptions
 * are read from the spike namespace resource at construction.
 */
export function createSpikeTestKeymapHarness(
  handlers: Partial<Record<SpikeCommandName, CommandHandler>>,
  i18n: I18nInstance,
): SpikeTestKeymapHarness {
  const commands = buildBuiltinCommands(i18n)
  const harness = createTestKeymap({ defaultKeys: true })

  const userHandlers = new Map<SpikeCommandName, CommandHandler>(
    Object.entries(handlers)
      .filter(([, v]) => v)
      .map(([k, v]) => [k as SpikeCommandName, v as CommandHandler]),
  )
  const lastDispatched: SpikeTestKeymapHarness["lastDispatched"] = {}

  const localCommands = commands.map(
    (spec): Command<TestKeymapTarget, TestKeymapEvent, CommandPayload> => ({
      name: spec.name,
      run(ctx: CommandContext<TestKeymapTarget, TestKeymapEvent, CommandPayload>) {
        lastDispatched.command = spec.name
        lastDispatched.payload = ctx.payload
        const handler = userHandlers.get(spec.name)
        if (!handler) return { ok: false as const, reason: "inactive" }
        return handler(ctx.payload ?? {})
      },
    }),
  )

  const layer: Layer<TestKeymapTarget, TestKeymapEvent> = {
    priority: 100,
    bindings: commands.map((spec) => ({
      key: spec.key,
      cmd: spec.name,
    })),
    commands: localCommands,
  }
  harness.keymap.registerLayer(layer)

  const surface: SpikeKeymap = {
    dispatch(name, payload) {
      const result = harness.keymap.runCommand(name, { payload })
      return result.ok
    },
    dispatchByKeymap(alias, payload) {
      const spec = commands.find((c) => c.key === alias)
      if (!spec) return false
      return harness.keymap.runCommand(spec.name, { payload }).ok
    },
    commandNames() {
      return commands.map((c) => c.name)
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
      const event = harness.host.press(parsed.name, {
        ...parsed.modifiers,
        ...modifiers,
      })
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
