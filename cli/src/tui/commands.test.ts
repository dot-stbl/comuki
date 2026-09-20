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
  allTuiCommands,
  buildApprovalCommands,
  buildTuiCommands,
  commandAvailable,
  completeSlashCommand,
  createTuiTestKeymapHarness,
  filterSlashCommands,
  fuzzyTier,
  paletteMatches,
  parseSlashInput,
  slashMenuQuery,
  TUI_UI_LAYER_PRIORITY,
  type TuiCommandContext,
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
      // Only keyed commands can collide on a chord — palette/slash-only
      // commands carry "" (no binding) by design.
      if (spec.key.length > 0) {
        expect(keys.has(spec.key)).toBe(false)
        keys.add(spec.key)
      }
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

describe("tui command registry — availability, slash, palette derivations", () => {
  const idleNoSession: TuiCommandContext = {
    sessionOpen: false,
    hasDraft: false,
    turnKind: "idle",
  }
  const openWithDraft: TuiCommandContext = {
    sessionOpen: true,
    hasDraft: true,
    turnKind: "idle",
  }
  const thinking: TuiCommandContext = {
    sessionOpen: true,
    hasDraft: false,
    turnKind: "thinking",
  }
  const awaitingApproval: TuiCommandContext = {
    sessionOpen: true,
    hasDraft: false,
    turnKind: "awaiting-approval",
  }

  test("availability tags gate listing surfaces once, from one context", async () => {
    const i18n = await createI18nFor("en")
    const byName = new Map(allTuiCommands(i18n).map((spec) => [spec.name, spec]))

    expect(commandAvailable(byName.get("exit")!, idleNoSession)).toBe(true)
    expect(commandAvailable(byName.get("submit-turn")!, idleNoSession)).toBe(false)
    expect(commandAvailable(byName.get("submit-turn")!, openWithDraft)).toBe(true)
    expect(commandAvailable(byName.get("close-session")!, idleNoSession)).toBe(false)
    expect(commandAvailable(byName.get("close-session")!, openWithDraft)).toBe(true)
    expect(commandAvailable(byName.get("cancel-turn")!, openWithDraft)).toBe(false)
    expect(commandAvailable(byName.get("cancel-turn")!, thinking)).toBe(true)
    expect(commandAvailable(byName.get("approve")!, thinking)).toBe(false)
    expect(commandAvailable(byName.get("approve")!, awaitingApproval)).toBe(true)
    expect(commandAvailable(byName.get("reject")!, awaitingApproval)).toBe(true)
  })

  test("slash menu query opens only for a leading, whitespace-free token", () => {
    expect(slashMenuQuery("hello")).toBeNull()
    expect(slashMenuQuery("")).toBeNull()
    expect(slashMenuQuery("/")).toBe("")
    expect(slashMenuQuery("/Re")).toBe("re")
    expect(slashMenuQuery("/rename ")).toBeNull()
    expect(slashMenuQuery("/rename x")).toBeNull()
  })

  test("slash filter matches names and aliases by prefix, availability-filtered", async () => {
    const i18n = await createI18nFor("en")
    const specs = allTuiCommands(i18n)
    const everything = filterSlashCommands(specs, "", openWithDraft)
    expect(everything.map((spec) => spec.slash?.name)).toEqual([
      "new",
      "close",
      "exit",
      "help",
      "rename",
      "clear-draft",
    ])

    const exitOnly = filterSlashCommands(specs, "q", openWithDraft)
    expect(exitOnly.map((spec) => spec.name)).toEqual(["exit"])

    // /stop is unavailable while idle — the menu does not offer it.
    expect(filterSlashCommands(specs, "stop", openWithDraft)).toHaveLength(0)
    expect(filterSlashCommands(specs, "stop", thinking).map((spec) => spec.name)).toEqual([
      "cancel-turn",
    ])
  })

  test("slash completion emits the canonical name plus one space", async () => {
    const i18n = await createI18nFor("en")
    const exit = allTuiCommands(i18n).find((spec) => spec.name === "exit")!
    expect(completeSlashCommand(exit)).toBe("/exit ")
  })

  test("slash input parsing resolves canonical names and aliases with args", async () => {
    const i18n = await createI18nFor("en")
    const specs = allTuiCommands(i18n)

    const rename = parseSlashInput("/rename   My   tab ", specs)
    expect(rename?.spec.name).toBe("rename-session")
    expect(rename?.args).toBe("My tab")

    const quit = parseSlashInput("/quit", specs)
    expect(quit?.spec.name).toBe("exit")
    expect(quit?.args).toBe("")

    const unknown = parseSlashInput("/definitely-not-a-command", specs)
    expect(unknown).toBeNull()

    expect(parseSlashInput("plain message", specs)).toBeNull()
    expect(parseSlashInput("/", specs)).toBeNull()
  })

  test("fuzzy tiers rank prefix over word-start over plain subsequence", () => {
    expect(fuzzyTier("cl", "clear-draft")).toBe(0)
    // 'd' lands after the '-' — a word start.
    expect(fuzzyTier("cd", "clear-draft")).toBe(1)
    // 'l' and 'e' are both mid-word — plain subsequence.
    expect(fuzzyTier("le", "clear-draft")).toBe(2)
    expect(fuzzyTier("zzz", "new-session")).toBeNull()
    expect(fuzzyTier("", "anything")).toBe(0)
  })

  test("palette matches filter by availability, tier and stay registry-stable", async () => {
    const i18n = await createI18nFor("en")
    const specs = allTuiCommands(i18n)

    const everything = paletteMatches(specs, "", openWithDraft)
    expect(everything.map((spec) => spec.name)).toContain("submit-turn")
    expect(everything.map((spec) => spec.name)).not.toContain("approve")

    // "re" ranks the prefix match rename-session before the plain
    // subsequence match clear-draft.
    const ranked = paletteMatches(specs, "re", openWithDraft)
    expect(ranked[0]?.name).toBe("rename-session")
    expect(ranked.map((spec) => spec.name)).toContain("clear-draft")

    expect(paletteMatches(specs, "qqqq", openWithDraft)).toHaveLength(0)
  })

  test("ui layers out-prioritize the approval layer constant", () => {
    expect(TUI_UI_LAYER_PRIORITY).toBeGreaterThan(200)
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
