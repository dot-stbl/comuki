/**
 * The shipped locale set — the single enumeration point for everything that
 * needs to know which languages exist (change `dashboard-i18n` D13).
 *
 * The registry drives the switcher, the lazy loader, the parity test's
 * locale list and the per-wave catalogue scaffold: adding a locale is one
 * entry plus its catalogues, removing one hides it everywhere at once.
 *
 * `endonym` is the display name and is deliberately *not* translated — a
 * person switches languages while not yet reading the one on screen, so
 * every option is named in the language it selects. `quality` is metadata
 * for tooling and docs, never chrome (D14): the switcher renders only the
 * endonym.
 */
export interface LocaleEntry {
  readonly code: string
  readonly endonym: string
  readonly quality: "source" | "reviewed" | "draft"
}

export const LOCALES: readonly LocaleEntry[] = [
  { code: "en", endonym: "English", quality: "source" },
  { code: "ru", endonym: "Русский", quality: "reviewed" },
  { code: "de", endonym: "Deutsch", quality: "draft" },
  { code: "es", endonym: "Español", quality: "draft" },
  { code: "fr", endonym: "Français", quality: "draft" },
  { code: "pt-BR", endonym: "Português (Brasil)", quality: "draft" },
  { code: "zh-CN", endonym: "简体中文", quality: "draft" },
  { code: "ja", endonym: "日本語", quality: "draft" },
  { code: "ko", endonym: "한국어", quality: "draft" },
  { code: "it", endonym: "Italiano", quality: "draft" },
  { code: "pl", endonym: "Polski", quality: "draft" },
  { code: "tr", endonym: "Türkçe", quality: "draft" },
  { code: "zh-TW", endonym: "中文（繁體）", quality: "draft" },
]

/** The boot default and the fallback chain's terminus (`dashboard-i18n` D3). */
export const DEFAULT_LOCALE = "en"

/** Sibling of `comuki-ui-theme`: the locale choice persists the same way. */
export const LOCALE_STORAGE_KEY = "comuki-locale"

export function isLocaleCode(value: unknown): value is string {
  return (
    typeof value === "string" && LOCALES.some((entry) => entry.code === value)
  )
}

/**
 * Every translation namespace. One per domain plus `shell` (layout, nav,
 * command palette), `kit` (shared/ui labels) and `common` (relative time,
 * permission denials, error fallbacks) — D1.
 */
export const NAMESPACES: readonly string[] = [
  "shell",
  "kit",
  "common",
  "projects",
  "runs",
  "tasks",
  "queue",
  "chat",
  "identity",
  "sources",
  "approvals",
  "cost",
  "compute",
  "home",
  "knowledge",
  "models",
  "observability",
  "verify",
  "settings",
  "inbox",
  "artifacts",
  "auth",
]
