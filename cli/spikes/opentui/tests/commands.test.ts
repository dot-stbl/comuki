/**
 * Command & keymap — palette and keybindings share one surface.
 *
 * The issue's architectural question is "open and close a command
 * palette from the same named command registry used by keybindings".
 * The proof below asserts: one named command + one counter handler,
 * invoked through palette/`dispatch(name)` AND through key chord/
 * `press(alias)`. Exactly two hits land in the counter.
 * Unbound keys do not invoke it.
 *
 * All dispatching goes through `@opentui/keymap`'s `Keymap.runCommand`
 * — there is no parallel `Map<string, handler>` shadow registry.
 */

import { test, expect, describe, afterEach } from "bun:test"
import {
  BUILTIN_COMMANDS,
  createSpikeTestKeymapHarness,
  type SpikeTestKeymapHarness,
} from "../src/commands/registry.js"

describe("command keymap — palette + key + unbound", () => {
  const harnesses: SpikeTestKeymapHarness[] = []

  function newHarness(
    handlers: Parameters<typeof createSpikeTestKeymapHarness>[0]
  ): SpikeTestKeymapHarness {
    const h = createSpikeTestKeymapHarness(handlers)
    harnesses.push(h)
    return h
  }

  afterEach(() => {
    for (const h of harnesses) h.cleanup()
    harnesses.length = 0
  })

  test("every builtin command has a unique name and a unique key", () => {
    const names = new Set<string>()
    const keys = new Set<string>()
    for (const spec of BUILTIN_COMMANDS) {
      expect(names.has(spec.name)).toBe(false)
      names.add(spec.name)
      expect(keys.has(spec.key)).toBe(false)
      keys.add(spec.key)
    }
  })

  test("press('ctrl+p') dispatches open-palette through the keymap", () => {
    let count = 0
    const harness = newHarness({
      "open-palette": () => {
        count += 1
      },
    })
    harness.press("ctrl+p")
    expect(count).toBe(1)
    expect(harness.lastDispatched.command).toBe("open-palette")
  })

  test("one counter handler — palette/name + key/chord = exactly two hits, same registry", () => {
    let hits = 0
    let lastPayloadText: string | undefined
    const harness = newHarness({
      "save-snippet": (p) => {
        hits += 1
        lastPayloadText = p.text
      },
    })

    // Path A — palette / named dispatch with a payload.
    const dispatchOk = harness.keymap.dispatch("save-snippet", {
      text: "from-name",
    })
    expect(dispatchOk).toBe(true)
    expect(hits).toBe(1)
    expect(lastPayloadText).toBe("from-name")
    expect(harness.lastDispatched.command).toBe("save-snippet")
    expect(harness.lastDispatched.payload?.text).toBe("from-name")

    // Path B — same registry, same layer, same command — fired by
    // a key chord routed through @opentui/keymap's resolver.
    const pressOk = harness.press("ctrl+s")
    expect(pressOk).toBe(true)
    expect(hits).toBe(2)
    // The key path routes through @opentui/keymap's binding
    // pipeline; user-supplied payloads travel in, but the binding
    // string itself does NOT auto-attach as `payload.text`.
    expect(harness.lastDispatched.command).toBe("save-snippet")
  })

  test("dispatch(name) and press(chord) reach the same handler", () => {
    let hits = 0
    const harness = newHarness({
      "save-snippet": () => {
        hits += 1
      },
    })

    expect(harness.keymap.dispatch("save-snippet")).toBe(true)
    expect(hits).toBe(1)

    hits = 0
    expect(harness.press("ctrl+s")).toBe(true)
    expect(hits).toBe(1)
    expect(harness.lastDispatched.command).toBe("save-snippet")
  })

  test("dispatchByKeymap(ctrl+s) reaches save-snippet and reaches no other", () => {
    let snippet = 0
    let palette = 0
    const harness = newHarness({
      "save-snippet": () => {
        snippet += 1
      },
      "open-palette": () => {
        palette += 1
      },
    })

    expect(harness.keymap.dispatchByKeymap("ctrl+s")).toBe(true)
    expect(snippet).toBe(1)
    expect(palette).toBe(0)
  })

  test("dispatch carries the payload to the handler", () => {
    let payload: { text?: string; reason?: string } | undefined
    const harness = newHarness({
      "queue-followup": (p) => {
        payload = p
      },
    })
    harness.keymap.dispatch("queue-followup", { text: "from chord" })
    expect(payload?.text).toBe("from chord")
  })

  test("dispatching an unbound name returns false (no throw)", () => {
    const harness = newHarness({})
    const result = harness.keymap.dispatch(
      "non-existent" as unknown as Parameters<typeof harness.keymap.dispatch>[0]
    )
    expect(result).toBe(false)
  })

  test("dispatchByKeymap of an unbound alias returns false (no throw)", () => {
    const harness = newHarness({})
    expect(harness.keymap.dispatchByKeymap("ctrl+does-not-exist")).toBe(false)
  })

  test("pressing an unbound key never invokes a named handler", () => {
    let count = 0
    const harness = newHarness({
      "approve-plan": () => {
        count += 1
      },
    })

    // Keys not in the BUILTIN_COMMANDS layer must not trigger the
    // approve-plan handler.
    harness.press("f10")
    harness.press("pageup")
    harness.press("z")
    expect(count).toBe(0)
  })

  test("commandNames() lists every builtin in registration order", () => {
    const harness = newHarness({})
    expect(harness.keymap.commandNames()).toEqual(
      BUILTIN_COMMANDS.map((s) => s.name)
    )
  })
})
