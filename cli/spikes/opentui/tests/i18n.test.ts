/**
 * i18n key parity test — i18next-driven.
 *
 * Asserts that every REQUIRED_KEYS resolves through a real
 * i18next instance for both locales (en + ru), the source of
 * truth (en) has at least that many keys, switching the locale
 * changes actual user-facing command labels / placeholder /
 * approval prefixes, and `instance.exists(key, {ns:'spike'})`
 * confirms the key is reachable through the spike namespace.
 */
import { describe, expect, test } from "bun:test"
import {
  createI18nFor,
  DEFAULT_LOCALE,
  LOCALES,
  REQUIRED_KEYS,
  EN,
  RU,
  SPIKE_NAMESPACE,
  tr,
  type I18nInstance,
  type LocaleCode,
} from "../src/locales/index.js"

async function freshI18n(locale: LocaleCode): Promise<I18nInstance> {
  return createI18nFor(locale)
}

describe("i18n — every required key is present in every locale", () => {
  test("REQUIRED_KEYS is non-empty", () => {
    expect(REQUIRED_KEYS.length).toBeGreaterThan(0)
  })

  test("LOCALES contains exactly en and ru", () => {
    expect([...LOCALES].sort()).toEqual(["en", "ru"])
  })

  test("default locale is en", () => {
    expect(DEFAULT_LOCALE).toBe("en")
  })

  test("spike namespace is registered", () => {
    expect(SPIKE_NAMESPACE).toBe("spike")
  })

  test("EN source-of-truth has every REQUIRED_KEYS key non-empty", () => {
    for (const key of REQUIRED_KEYS) {
      const value = lookup(EN, key)
      expect(value).toBeDefined()
      expect(typeof value).toBe("string")
      expect((value as string).length).toBeGreaterThan(0)
    }
  })

  test("RU parallel has every REQUIRED_KEYS key non-empty", () => {
    for (const key of REQUIRED_KEYS) {
      const value = lookup(RU, key)
      expect(value).toBeDefined()
      expect(typeof value).toBe("string")
      expect((value as string).length).toBeGreaterThan(0)
    }
  })

  test("EN and RU differ in user-facing copy (real localisation, not stubs)", () => {
    let diffCount = 0
    for (const key of REQUIRED_KEYS) {
      const en = lookup(EN, key)
      const ru = lookup(RU, key)
      if (en !== undefined && ru !== undefined && en !== ru) diffCount += 1
    }
    expect(diffCount).toBeGreaterThan(REQUIRED_KEYS.length / 2)
  })

  test("tr(i18n, key) returns the active locale value for en", async () => {
    const en = await freshI18n("en")
    expect(tr(en, "composer.placeholder")).toBe(
      "Ask Comuki. Use / for actions or @ for knowledge.",
    )
  })

  test("tr(i18n, key) returns the active locale value for ru", async () => {
    const ru = await freshI18n("ru")
    expect(tr(ru, "composer.placeholder")).toBe(
      "Спроси Comuki. / для действий, @ для знаний.",
    )
  })

  test("switching locale changes command labels, placeholder, and approval prefixes", async () => {
    const en = await freshI18n("en")
    const ru = await freshI18n("ru")

    expect(tr(en, "cmd.open-palette.label")).toBe("Open command palette")
    expect(tr(ru, "cmd.open-palette.label")).toBe("Открыть палитру команд")

    expect(tr(en, "cmd.save-snippet.label")).toBe(
      "Save composer draft as snippet",
    )
    expect(tr(ru, "cmd.save-snippet.label")).toBe(
      "Сохранить черновик как сниппет",
    )

    expect(tr(en, "composer.placeholder")).not.toBe(tr(ru, "composer.placeholder"))
    expect(tr(en, "approval.decideLabel")).not.toBe(tr(ru, "approval.decideLabel"))
    expect(tr(en, "approval.action.approve")).not.toBe(
      tr(ru, "approval.action.approve"),
    )
    expect(tr(en, "approval.action.reject")).not.toBe(
      tr(ru, "approval.action.reject"),
    )
  })

  test("approval prefixes are neutral (no REACT- / CORE- / React- artifacts)", async () => {
    const en = await freshI18n("en")
    const ru = await freshI18n("ru")
    const prefixes = [
      "approval.intentPrefix",
      "approval.scopePrefix",
      "approval.riskPrefix",
      "approval.planPrefix",
      "approval.stepPrefix",
      "approval.diffPrefix",
    ] as const
    for (const key of prefixes) {
      const enValue = tr(en, key)
      const ruValue = tr(ru, key)
      expect(enValue.startsWith("APPROVAL-")).toBe(true)
      expect(ruValue.startsWith("APPROVAL-")).toBe(true)
      expect(enValue.includes("REACT")).toBe(false)
      expect(ruValue.includes("REACT")).toBe(false)
    }
  })

  test("i18next exists({ns:'spike'}) confirms every REQUIRED_KEYS key resolves", async () => {
    const en = await freshI18n("en")
    const ru = await freshI18n("ru")
    for (const key of REQUIRED_KEYS) {
      expect(en.exists(key, { ns: SPIKE_NAMESPACE })).toBe(true)
      expect(ru.exists(key, { ns: SPIKE_NAMESPACE })).toBe(true)
    }
  })

  test("command identity (BUILTIN_COMMANDS.name) is not in the locale resource", () => {
    // Names like "open-palette" / "approve-plan" stay as programming
    // handles — the locale only carries user-facing labels and
    // descriptions. This guards against accidentally translating
    // a keymap command name.
    expect(lookup(EN, "cmd.open-palette.name")).toBeUndefined()
    expect(lookup(EN, "name.open-palette")).toBeUndefined()
  })
})

/**
 * Walk the nested locale resource by dotted key path.
 * "cmd.open-palette.label" → EN.cmd["open-palette"].label
 * "chrome.title" → EN.chrome.title
 */
function lookup(resource: unknown, dotted: string): string | undefined {
  let cur: unknown = resource
  for (const seg of dotted.split(".")) {
    if (cur && typeof cur === "object" && seg in (cur as Record<string, unknown>)) {
      cur = (cur as Record<string, unknown>)[seg]
    } else {
      return undefined
    }
  }
  return typeof cur === "string" ? cur : undefined
}
