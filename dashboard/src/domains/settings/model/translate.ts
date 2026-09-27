import { i18n } from "@/shared/i18n"

/**
 * A `settings`-namespace translator, the shape the word-bearing column
 * factories resolve their copy through (`dashboard-i18n` D7). A `cell` is a
 * plain function TanStack calls while it builds a row, so a factory cannot
 * call `useTranslation` — the panel passes its hook-bound `t` in, and callers
 * with no translator of their own fall back to the shared instance, which
 * answers in the active locale.
 */
export type SettingsTranslator = (
  key: string,
  options?: Record<string, unknown>
) => string

export function sharedSettingsT(
  key: string,
  options?: Record<string, unknown>
): string {
  return i18n.t(key, { ...options, ns: "settings" })
}
