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

export interface TuiCommandSpec {
  readonly name: TuiCommandName
  /** Default binding string used by the default layer. */
  readonly key: string
  readonly label: string
  readonly description: string
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
    command(i18n, "submit-turn", "return"),
    command(i18n, "cancel-turn", "escape"),
    command(i18n, "new-session", "ctrl+n"),
    command(i18n, "close-session", "ctrl+w"),
    command(i18n, "exit", "ctrl+c"),
  ]
}

/** The approval-only commands — bound to plain y/n in their own layer. */
export function buildApprovalCommands(i18n: I18nInstance): readonly TuiCommandSpec[] {
  return [command(i18n, "approve", "y"), command(i18n, "reject", "n")]
}

function command(
  i18n: I18nInstance,
  name: TuiCommandName,
  key: string
): TuiCommandSpec {
  return {
    name,
    key,
    label: tr(i18n, `cmd.${name}.label`),
    description: tr(i18n, `cmd.${name}.description`),
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
  destroy(): void
}

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
      spec.name === "submit-turn"
        ? [
            { key: spec.key, cmd: spec.name },
            { key: "kpenter", cmd: spec.name },
          ]
        : [{ key: spec.key, cmd: spec.name }]
    ),
    commands,
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
  const commandByKey = new Map(commands.map((spec) => [spec.key, spec]))

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
    bindings: commands.map((spec) => ({ key: spec.key, cmd: spec.name })),
    commands: localCommands(commands, handlers),
  })

  const commandByKey = new Map(commands.map((spec) => [spec.key, spec]))

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
        bindings: specs.map((spec) => ({ key: spec.key, cmd: spec.name })),
        commands: localCommands(specs, approvalHandlers),
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
