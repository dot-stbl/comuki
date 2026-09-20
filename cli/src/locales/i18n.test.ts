/**
 * i18n parity — the locale module moved from the spike
 * (`cli/spikes/opentui/src/locales`) into `cli/src/locales` (issue
 * #73); these tests assert the move kept the contract:
 *
 * - every REQUIRED_KEY exists in EVERY locale on the `tui` namespace;
 * - `tr` resolves every key to a non-empty, non-identity string;
 * - en and ru actually differ on user-facing copy;
 * - the parity holds for both a default instance and a fresh
 *   non-default locale instance.
 */

import { describe, expect, test } from "bun:test"
import {
  createI18nFor,
  DEFAULT_LOCALE,
  LOCALES,
  REQUIRED_KEYS,
  RESOURCES,
  TUI_NAMESPACE,
  tr,
} from "./index"

describe("tui locales — parity between en and ru", () => {
  test("every required key exists in every locale", async () => {
    for (const locale of LOCALES) {
      const instance = await createI18nFor(locale)
      for (const key of REQUIRED_KEYS) {
        expect(instance.exists(key, { ns: TUI_NAMESPACE })).toBe(true)
      }
    }
  })

  test("tr resolves every required key to a non-empty value", async () => {
    for (const locale of LOCALES) {
      const instance = await createI18nFor(locale)
      for (const key of REQUIRED_KEYS) {
        expect(() => tr(instance, key)).not.toThrow()
        expect(tr(instance, key).length).toBeGreaterThan(0)
      }
    }
  })

  test("tr throws on a key the resource does not carry", async () => {
    const instance = await createI18nFor(DEFAULT_LOCALE)
    expect(() => tr(instance, "definitely.not.a.key")).toThrow()
  })

  test("en and ru differ on user-facing copy", async () => {
    const en = await createI18nFor("en")
    const ru = await createI18nFor("ru")
    const samples = [
      "composer.placeholder",
      "transcript.you",
      "transcript.failed",
      "transcript.emptySession",
      "approval.action.approve",
      "approval.action.reject",
      "approval.decideLabel",
      "cmd.submit-turn.label",
      "cmd.exit.description",
      "cmd.help.label",
      "cmd.rename-session.argsHint",
      "cmd.clear-draft.description",
      "cmd.open-editor.label",
      "cmd.open-palette.label",
      "menu.commandsTitle",
      "menu.commandsHint",
      "palette.title",
      "palette.placeholder",
      "palette.empty",
      "help.title",
      "queue.prefix",
      "queue.moreSuffix",
    ] as const
    for (const key of samples) {
      expect(tr(en, key)).not.toBe(tr(ru, key))
    }
  })

  test("resource trees carry the same nested key set", () => {
    expect(flattenKeys(RESOURCES.en).sort()).toEqual(
      flattenKeys(RESOURCES.ru).sort()
    )
  })

  test("a fresh ru instance is fully initialized (async init awaited)", async () => {
    const ru = await createI18nFor("ru")
    expect(ru.isInitialized).toBe(true)
    expect(ru.language).toBe("ru")
    expect(tr(ru, "approval.action.approve")).toBe("одобрить")
  })
})

function flattenKeys(value: unknown, prefix = ""): string[] {
  if (value === null || typeof value !== "object") {
    return [prefix]
  }
  return Object.entries(value as Record<string, unknown>).flatMap(
    ([key, nested]) => flattenKeys(nested, prefix === "" ? key : `${prefix}.${key}`)
  )
}
