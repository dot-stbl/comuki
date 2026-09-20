/**
 * Command & keymap semantics for the production registry — adapted
 * from the spike's `commands.test.ts` proof, with the approval-layer
 * gating the spike did not need:
 *
 * - the base layer (enter / escape / ctrl+n / ctrl+w / ctrl+c) always
 *   resolves through the SAME registry (`runCommand`), exactly once;
 * - plain y/n are INERT until the host registers the approval layer —
 *   typing "y" into the composer must stay a keystroke, not a command;
 * - unbound keys never fire a named handler;
 * - names/keys are unique and stable across locales; only labels and
 *   descriptions localize.
 */

import { afterEach, describe, expect, test } from "bun:test"
import {
  buildApprovalCommands,
  buildTuiCommands,
  createTuiTestKeymapHarness,
  type TuiTestKeymapHarness,
} from "./commands"
import { createI18nFor, DEFAULT_LOCALE, type LocaleCode } from "../locales"

const harnesses: TuiTestKeymapHarness[] = []

async function newHarness(
  handlers: Parameters<typeof createTuiTestKeymapHarness>[0],
  locale: LocaleCode = DEFAULT_LOCALE
): Promise<TuiTestKeymapHarness> {
  const i18n = await createI18nFor(locale)
  const harness = createTuiTestKeymapHarness(handlers, i18n)
  harnesses.push(harness)
  return harness
}

afterEach(() => {
  for (const harness of harnesses) {
    harness.cleanup()
  }
  harnesses.length = 0
})

describe("tui command registry — names, keys, locales", () => {
  test("base + approval commands have unique names and unique keys", async () => {
    const i18n = await createI18nFor("en")
    const all = [...buildTuiCommands(i18n), ...buildApprovalCommands(i18n)]
    const names = new Set<string>()
    const keys = new Set<string>()
    for (const spec of all) {
      expect(names.has(spec.name)).toBe(false)
      names.add(spec.name)
      expect(keys.has(spec.key)).toBe(false)
      keys.add(spec.key)
    }
  })

  test("command names are identical between en and ru; labels localize", async () => {
    const en = await createI18nFor("en")
    const ru = await createI18nFor("ru")
    const enSpecs = buildTuiCommands(en)
    const ruSpecs = buildTuiCommands(ru)
    expect(enSpecs.length).toBe(ruSpecs.length)
    let localized = 0
    for (let index = 0; index < enSpecs.length; index += 1) {
      const enSpec = enSpecs[index]!
      const ruSpec = ruSpecs[index]!
      expect(ruSpec.name).toBe(enSpec.name)
      expect(ruSpec.key).toBe(enSpec.key)
      if (ruSpec.label !== enSpec.label || ruSpec.description !== enSpec.description) {
        localized += 1
      }
    }
    expect(localized).toBeGreaterThan(0)
  })
})

describe("tui keymap — base layer dispatch", () => {
  test("press('return') dispatches submit-turn exactly once", async () => {
    let hits = 0
    const harness = await newHarness({
      "submit-turn": () => {
        hits += 1
      },
    })
    expect(harness.press("return")).toBe(true)
    expect(hits).toBe(1)
    expect(harness.lastDispatched.command).toBe("submit-turn")
  })

  test("named dispatch + key chord reach the SAME handler — exactly two hits", async () => {
    let hits = 0
    const payloads: Array<{ text?: string } | undefined> = []
    const harness = await newHarness({
      "submit-turn": (payload) => {
        hits += 1
        payloads.push(payload)
      },
    })
    expect(harness.keymap.dispatch("submit-turn", { text: "from-name" })).toBe(true)
    expect(hits).toBe(1)
    expect(payloads[0]?.text).toBe("from-name")

    expect(harness.press("return")).toBe(true)
    expect(hits).toBe(2)
    expect(harness.lastDispatched.command).toBe("submit-turn")
  })

  test("escape → cancel-turn, ctrl+n → new-session, ctrl+w → close-session, ctrl+c → exit", async () => {
    const fired: string[] = []
    const harness = await newHarness({
      "cancel-turn": () => {
        fired.push("cancel-turn")
      },
      "new-session": () => {
        fired.push("new-session")
      },
      "close-session": () => {
        fired.push("close-session")
      },
      exit: () => {
        fired.push("exit")
      },
    })
    expect(harness.press("escape")).toBe(true)
    expect(harness.press("ctrl+n")).toBe(true)
    expect(harness.press("ctrl+w")).toBe(true)
    expect(harness.press("ctrl+c")).toBe(true)
    expect(fired).toEqual(["cancel-turn", "new-session", "close-session", "exit"])
  })

  test("unbound keys never invoke a named handler", async () => {
    let hits = 0
    const harness = await newHarness({
      "submit-turn": () => {
        hits += 1
      },
      exit: () => {
        hits += 1
      },
    })
    harness.press("f10")
    harness.press("pageup")
    harness.press("z")
    expect(hits).toBe(0)
  })

  test("dispatch of an unbound name returns false (no throw)", async () => {
    const harness = await newHarness({})
    expect(
      harness.keymap.dispatch("nope" as never)
    ).toBe(false)
  })
})

describe("tui keymap — approval layer gating", () => {
  test("plain y/n are inert while no approval layer is registered", async () => {
    const harness = await newHarness({})
    harness.press("y")
    harness.press("n")
    // No approval layer is registered, so the presses resolve to no
    // command at all — assert through the keymap, not dead counters.
    expect(harness.lastDispatched.command).toBeUndefined()
  })

  test("registered approval layer routes y → approve and n → reject exactly once each", async () => {
    const decisions: boolean[] = []
    const harness = await newHarness({})
    const unregister = harness.keymap.registerApprovalLayer({
      approve: () => {
        decisions.push(true)
      },
      reject: () => {
        decisions.push(false)
      },
    })
    expect(harness.press("y")).toBe(true)
    expect(harness.press("n")).toBe(true)
    expect(decisions).toEqual([true, false])

    unregister()
    decisions.length = 0
    harness.press("y")
    harness.press("n")
    expect(decisions).toEqual([])
  })

  test("the approval layer does not disturb base-layer chords", async () => {
    let exits = 0
    const harness = await newHarness({
      exit: () => {
        exits += 1
      },
    })
    harness.keymap.registerApprovalLayer({
      approve: () => {},
      reject: () => {},
    })
    harness.press("ctrl+c")
    expect(exits).toBe(1)
  })
})
