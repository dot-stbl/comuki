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
 *
 * The harness takes a fully-initialized `i18n` instance from
 * `createI18nFor`; tests exercise both locales to prove the
 * localized command labels differ and flow through `buildBuiltinCommands`.
 */

import { afterEach, describe, expect, test } from "bun:test"
import {
  buildBuiltinCommands,
  createSpikeTestKeymapHarness,
  type SpikeTestKeymapHarness,
} from "../src/commands/registry.js"
import {
  createI18nFor,
  DEFAULT_LOCALE,
  type LocaleCode,
} from "../src/locales/index.js"

const harnesses: SpikeTestKeymapHarness[] = []

async function newHarness(
  handlers: Parameters<typeof createSpikeTestKeymapHarness>[0],
  locale: LocaleCode = DEFAULT_LOCALE,
): Promise<SpikeTestKeymapHarness> {
  const i18n = await createI18nFor(locale)
  const h = createSpikeTestKeymapHarness(handlers, i18n)
  harnesses.push(h)
  return h
}

afterEach(() => {
  for (const h of harnesses) h.cleanup()
  harnesses.length = 0
})

describe("command keymap — palette + key + unbound", () => {
  test("every builtin command has a unique name and a unique key", async () => {
    const i18n = await createI18nFor("en")
    const names = new Set<string>()
    const keys = new Set<string>()
    for (const spec of buildBuiltinCommands(i18n)) {
      expect(names.has(spec.name)).toBe(false)
      names.add(spec.name)
      expect(keys.has(spec.key)).toBe(false)
      keys.add(spec.key)
    }
  })

  test("buildBuiltinCommands labels differ between en and ru", async () => {
    const en = await createI18nFor("en")
    const ru = await createI18nFor("ru")
    const enSpecs = buildBuiltinCommands(en)
    const ruSpecs = buildBuiltinCommands(ru)

    expect(enSpecs.length).toBe(ruSpecs.length)
    let diffCount = 0
    for (let i = 0; i < enSpecs.length; i += 1) {
      const enSpec = enSpecs[i]!
      const ruSpec = ruSpecs[i]!
      expect(ruSpec.name).toBe(enSpec.name)
      expect(ruSpec.key).toBe(enSpec.key)
      if (ruSpec.label !== enSpec.label) diffCount += 1
      if (ruSpec.description !== enSpec.description) diffCount += 1
    }
    // Most labels and descriptions should change between locales.
    expect(diffCount).toBeGreaterThan(enSpecs.length)
  })

  test("press('ctrl+p') dispatches open-palette through the keymap", async () => {
    let dispatched: string | undefined
    const harness = await newHarness({
      "open-palette": () => {
        dispatched = "open-palette"
      },
    })
    harness.press("ctrl+p")
    expect(dispatched).toBe("open-palette")
    expect(harness.lastDispatched.command).toBe("open-palette")
  })

  test("one counter handler — palette/name + key/chord = exactly two hits, same registry", async () => {
    let hits = 0
    const payloadSeen: Array<{ text?: string } | undefined> = []
    const harness = await newHarness({
      "save-snippet": (p) => {
        hits += 1
        payloadSeen.push(p)
      },
    })

    // Path A — palette / named dispatch with a payload.
    expect(harness.keymap.dispatch("save-snippet", { text: "from-name" })).toBe(true)
    expect(hits).toBe(1)
    expect(payloadSeen[0]?.text).toBe("from-name")

    // Path B — same registry, same layer, same command — fired by
    // a key chord routed through @opentui/keymap's resolver.
    expect(harness.press("ctrl+s")).toBe(true)
    expect(hits).toBe(2)
    expect(harness.lastDispatched.command).toBe("save-snippet")
  })

  test("dispatchByKeymap(ctrl+s) reaches save-snippet and reaches no other", async () => {
    let snippet = 0
    let palette = 0
    const harness = await newHarness({
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

  test("dispatch carries the payload to the handler", async () => {
    let payload: { text?: string } | undefined
    const harness = await newHarness({
      "queue-followup": (p) => {
        payload = p
      },
    })
    harness.keymap.dispatch("queue-followup", { text: "from chord" })
    expect(payload?.text).toBe("from chord")
  })

  test("dispatching an unbound name returns false (no throw)", async () => {
    const harness = await newHarness({})
    const result = harness.keymap.dispatch(
      "non-existent" as unknown as Parameters<typeof harness.keymap.dispatch>[0],
    )
    expect(result).toBe(false)
  })

  test("dispatchByKeymap of an unbound alias returns false (no throw)", async () => {
    const harness = await newHarness({})
    expect(harness.keymap.dispatchByKeymap("ctrl+does-not-exist")).toBe(false)
  })

  test("pressing an unbound key never invokes a named handler", async () => {
    let count = 0
    const harness = await newHarness({
      "approve-plan": () => {
        count += 1
      },
    })

    harness.press("f10")
    harness.press("pageup")
    harness.press("z")
    expect(count).toBe(0)
  })

  test("commandNames() lists every builtin in registration order", async () => {
    const i18n = await createI18nFor("en")
    const harness = await newHarness({})
    expect(harness.keymap.commandNames()).toEqual(
      buildBuiltinCommands(i18n).map((s) => s.name),
    )
  })
})
