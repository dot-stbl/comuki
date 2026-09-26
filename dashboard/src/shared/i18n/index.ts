/**
 * The dashboard's one translation instance (`dashboard-i18n` D4/D7/D8).
 *
 * `en` is imported eagerly as native Vite JSON modules — it must be
 * synchronous because it is the boot default, the fallback chain's
 * terminus and the test language; an async default would flash key paths
 * on first paint. Every other locale ships as a lazy, fingerprinted
 * chunk and lands through `loadLocale`, which awaits the locale's
 * namespace imports and `addResourceBundle`s them before the caller
 * switches. An operator who never switches downloads exactly one
 * locale's payload.
 *
 * Non-component modules (label maps, validators, toast call sites) use
 * this instance directly — `i18n.t("projects:slug.error.spaces")` —
 * because they cannot call `useTranslation` (D7). Components use
 * `useTranslation("<ns>")` and address keys namespace-relative.
 *
 * Missing keys are loud by construction: `fallbackLng` answers from
 * English, and a key missing everywhere renders its full `ns:key.sub`
 * path — never a blank, never a crash (D8).
 */
import i18next from "i18next"
import { initReactI18next } from "react-i18next"

import { DEFAULT_LOCALE, isLocaleCode, LOCALE_STORAGE_KEY } from "./locales"

export {
  DEFAULT_LOCALE,
  isLocaleCode,
  LOCALES,
  LOCALE_STORAGE_KEY,
} from "./locales"
export type { LocaleEntry } from "./locales"

/* `en` rides the main bundle: one eager glob, resources inline. */
const enModules = import.meta.glob("./locales/en/*.json", {
  eager: true,
  import: "default",
}) as Record<string, Record<string, unknown>>

/* Every non-default locale stays a lazy chunk; `loadLocale` is the only
   door in. The negative pattern keeps `en` from getting a second,
   dynamic-import copy of modules it already carries eagerly. */
const lazyModules = import.meta.glob(
  ["./locales/*/*.json", "!./locales/en/*.json"],
  { import: "default" }
) as Record<string, () => Promise<Record<string, unknown>>>

/** `./locales/ru/projects.json` → `ru` */
function localeOf(path: string): string {
  return path.split("/")[2] ?? ""
}

/** `./locales/ru/projects.json` → `projects` */
function namespaceOf(path: string): string {
  const file = path.split("/").pop() ?? ""
  return file.replace(/\.json$/, "")
}

const enResources: Record<string, Record<string, unknown>> = {}
for (const [path, content] of Object.entries(enModules)) {
  enResources[namespaceOf(path)] = content
}

void i18next.use(initReactI18next).init({
  lng: DEFAULT_LOCALE,
  fallbackLng: DEFAULT_LOCALE,
  defaultNS: "shell",
  // i18next-native forms: `keySeparator` walks the tree, `pluralSeparator`
  // lets a plural group read `count.one` / `count.other` (D2), with
  // `few`/`many` per locale grammar.
  keySeparator: ".",
  nsSeparator: ":",
  pluralSeparator: ".",
  // React already escapes interpolated values; a second HTML-escape pass
  // would double every ampersand.
  interpolation: { escapeValue: false },
  returnNull: false,
  appendNamespaceToMissingKey: true,
  parseMissingKeyHandler: (key) => key,
  debug: import.meta.env.DEV,
  // No backend exists — everything is inline or chunk-imported — so init
  // resolves synchronously and the first `t()` call of the first paint
  // already has its answer.
  resources: { [DEFAULT_LOCALE]: enResources },
})

/** The one instance; `I18nextProvider` and `useTranslation` both land here. */
export const i18n = i18next

const loadedLocales = new Set<string>([DEFAULT_LOCALE])

/**
 * Lands a non-default locale's catalogues in the instance.
 *
 * Idempotent: a locale that has already been loaded resolves immediately,
 * so a switcher can call it on every selection without guarding. Does not
 * switch — the caller owns `changeLanguage`, which is what re-renders.
 */
export async function loadLocale(lng: string): Promise<void> {
  if (!isLocaleCode(lng) || loadedLocales.has(lng)) {
    return
  }

  const imports: Array<Promise<void>> = []
  for (const [path, load] of Object.entries(lazyModules)) {
    if (localeOf(path) !== lng) {
      continue
    }
    imports.push(
      load().then((content) => {
        i18next.addResourceBundle(lng, namespaceOf(path), content, true, true)
      })
    )
  }
  await Promise.all(imports)
  loadedLocales.add(lng)
}

/**
 * The stored choice, or `null` when nothing (or nothing usable) was kept.
 * Storage denied is a browser that forgets, not a browser that breaks —
 * the same bargain the theme makes.
 */
export function readStoredLocale(): string | null {
  try {
    return window.localStorage.getItem(LOCALE_STORAGE_KEY)
  } catch {
    return null
  }
}

function writeStoredLocale(code: string): void {
  try {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, code)
  } catch {
    // The choice holds for this session and is forgotten on reload.
  }
}

/**
 * Persists a choice and applies it. Split from `loadLocale` so the app
 * boot can reuse exactly the path the switcher takes.
 */
export async function activateLocale(code: string): Promise<void> {
  if (!isLocaleCode(code) || code === i18next.language) {
    return
  }
  await loadLocale(code)
  await i18next.changeLanguage(code)
  writeStoredLocale(code)
}

/**
 * Applies whatever the operator kept from their last visit. Called once
 * from the providers' effect: the default boot needs no locale fetch, and
 * a stored non-default locale may show one English frame while its chunk
 * loads — the same first-paint posture the theme accepts (D12).
 */
export async function activateStoredLocale(): Promise<void> {
  const stored = readStoredLocale()
  if (stored === null || stored === DEFAULT_LOCALE) {
    return
  }
  await activateLocale(stored)
}

if (typeof document !== "undefined") {
  document.documentElement.lang = i18next.language
  i18next.on("languageChanged", (lng) => {
    document.documentElement.lang = lng
  })
}
