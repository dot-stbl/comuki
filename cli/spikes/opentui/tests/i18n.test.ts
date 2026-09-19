/**
 * i18n key parity test.
 *
 * Asserts that every REQUIRED_KEYS is present in every locale
 * (en + ru), and that the source of truth (en) has at least that
 * many. This is the "minimal locale module and tests" the audit
 * asks for.
 */
import { describe, expect, test } from "bun:test"
import {
  EN,
  LOCALES,
  REQUIRED_KEYS,
  RESOURCES,
  RU,
  t,
  tr,
} from "../src/locales/index.js"

describe("i18n — every required key is present in every locale", () => {
  test("REQUIRED_KEYS is non-empty", () => {
    expect(REQUIRED_KEYS.length).toBeGreaterThan(0)
  })

  test("LOCALES contains exactly en and ru", () => {
    expect([...LOCALES].sort()).toEqual(["en", "ru"])
  })

  test("EN contains every REQUIRED_KEYS key", () => {
    for (const key of REQUIRED_KEYS) {
      expect(EN[key]).toBeDefined()
      expect(typeof EN[key]).toBe("string")
      expect((EN[key] ?? "").length).toBeGreaterThan(0)
    }
  })

  test("RU contains every REQUIRED_KEYS key", () => {
    for (const key of REQUIRED_KEYS) {
      expect(RU[key]).toBeDefined()
      expect(typeof RU[key]).toBe("string")
      expect((RU[key] ?? "").length).toBeGreaterThan(0)
    }
  })

  test("every locale resource has every REQUIRED_KEYS key (parity check)", () => {
    for (const locale of LOCALES) {
      const resource = RESOURCES[locale]
      for (const key of REQUIRED_KEYS) {
        expect(resource[key]).toBeDefined()
        expect(typeof resource[key]).toBe("string")
        expect((resource[key] ?? "").length).toBeGreaterThan(0)
      }
    }
  })

  test("EN and RU are not bit-identical (the spike actually localises, not stubs)", () => {
    const enKeys = Object.keys(EN)
    const ruKeys = Object.keys(RU)
    let diffCount = 0
    for (const key of enKeys) {
      if (RU[key] !== undefined && RU[key] !== EN[key]) diffCount += 1
    }
    expect(diffCount).toBeGreaterThan(0)
    // Every EN key must have a RU counterpart
    for (const key of enKeys) {
      expect(RU[key]).toBeDefined()
    }
    expect(ruKeys.length).toBe(enKeys.length)
  })

  test("t(key, locale) returns the locale value", () => {
    expect(t("composer.placeholder", "en")).toBe(EN["composer.placeholder"])
    expect(t("composer.placeholder", "ru")).toBe(RU["composer.placeholder"])
  })

  test("tr(key) returns the default-locale value", () => {
    expect(tr("composer.placeholder")).toBe(EN["composer.placeholder"])
  })

  test("command identity (BUILTIN_COMMANDS.name) is not in the locale", () => {
    // Names like "open-palette" / "approve-plan" stay as programming
    // handles — the locale only carries user-facing labels and
    // descriptions. This guards against accidentally translating
    // a keymap command name.
    expect(EN["cmd.open-palette.name" as keyof typeof EN]).toBeUndefined()
    expect(EN["name.open-palette" as keyof typeof EN]).toBeUndefined()
  })
})
