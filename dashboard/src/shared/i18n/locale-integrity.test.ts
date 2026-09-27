import { describe, expect, it } from "vitest"

import { LOCALES, NAMESPACES } from "./locales"

/**
 * Locale integrity (`dashboard-i18n` D6/D14 hardening): parity proves the
 * key SETS match; this suite proves the VALUES cannot drift in the ways
 * machine translation actually breaks things —
 *
 * - a dropped or renamed `{{placeholder}}` renders a literal blank in the
 *   sentence that receives it;
 * - a lost `<slot/>` silently empties a styled fragment;
 * - markup beyond the sanctioned inline vocabulary defeats the "markup
 *   lives in components" rule (D9) — the vocabulary is the `<slot/>`
 *   self-closing tags plus the paired `<code>`/`<em>`/`<tag>` inline tags
 *   the en source ships; anything else (and bare math `<`/`>` that only
 *   LOOK like tags is fine) is a failure;
 * - a plural group missing its grammar's forms falls through to `other`
 *   for counts it should decline;
 * - a namespace file or whole locale directory appearing or disappearing
 *   must follow the registry, not the filesystem.
 *
 * Plural groups are compared as a unit — a collapsed-grammar locale
 * (one `other` form) legitimately lacks the `.one` leaf its source
 * carries, so placeholder/slot signatures are unions over the group's
 * forms, never per-form.
 *
 * Registry-driven: adding locale #14 or namespace #23 requires zero edits
 * here. The missing-key fallback path is NOT re-tested — see
 * `spec-scenarios.test.tsx` ("renders the full ns:key path").
 */

const PLURAL_SUFFIXES = new Set([
  "zero",
  "one",
  "two",
  "few",
  "many",
  "other",
])

/**
 * The plural forms each shipped language's grammar demands (CLDR classes
 * i18next applies). Mirror of the structure the waves landed; if a new
 * locale joins, add its row or the suite fails with the actual set.
 */
const PLURAL_FORMS: Readonly<Record<string, readonly string[]>> = {
  en: ["one", "other"],
  ru: ["few", "many", "one", "other"],
  pl: ["few", "many", "one", "other"],
  it: ["many", "one", "other"],
  de: ["one", "other"],
  es: ["one", "other"],
  fr: ["one", "other"],
  "pt-BR": ["one", "other"],
  "zh-CN": ["other"],
  "zh-TW": ["other"],
  ja: ["other"],
  ko: ["other"],
  tr: ["other"],
}

/** Inline tags the en source's values legitimately carry (D9 vocabulary). */
const SANCTIONED_INLINE_TAG = /<\/?(?:code|em|tag)>/g
const TAG_LIKE = /<[A-Za-z!/]/

const PLACEHOLDER = /\{\{\s*([A-Za-z][\w.-]*)\s*\}\}/g
const SLOT = /<([A-Za-z][\w.-]*)\s*\/>/g

interface GroupEntry {
  forms: string[]
  values: string[]
}

interface LocaleTree {
  /** Plain-string leaves keyed by full path. */
  strings: Map<string, string>
  /** Plural groups keyed by full path. */
  groups: Map<string, GroupEntry>
}

const files = import.meta.glob("./locales/*/*.json", {
  eager: true,
  import: "default",
}) as Record<string, Record<string, unknown>>

const registryCodes = new Set(LOCALES.map((entry) => entry.code))

const catalogues = new Map<string, Map<string, Record<string, unknown>>>()
const directoryOf = new Set<string>()
for (const [path, content] of Object.entries(files)) {
  const parts = path.split("/")
  const lng = parts[2] ?? ""
  const ns = (parts[3] ?? "").replace(/\.json$/, "")
  directoryOf.add(lng)
  const tree = catalogues.get(lng) ?? new Map()
  tree.set(ns, content)
  catalogues.set(lng, tree)
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value)
}

function isPluralGroup(value: unknown): value is Record<string, string> {
  if (!isPlainObject(value)) {
    return false
  }
  const keys = Object.keys(value)
  return (
    keys.length > 0 &&
    keys.every((key) => PLURAL_SUFFIXES.has(key)) &&
    Object.values(value).every((entry) => typeof entry === "string")
  )
}

function collect(
  tree: Record<string, unknown>,
  prefix: string,
  out: LocaleTree
): void {
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix === "" ? key : `${prefix}.${key}`
    if (isPluralGroup(value)) {
      out.groups.set(path, {
        forms: Object.keys(value).sort(),
        values: Object.values(value),
      })
      continue
    }
    if (typeof value === "string") {
      out.strings.set(path, value)
      continue
    }
    if (isPlainObject(value)) {
      collect(value, path, out)
    }
  }
}

function flattenLocale(code: string): LocaleTree {
  const out: LocaleTree = { strings: new Map(), groups: new Map() }
  for (const [ns, tree] of catalogues.get(code) ?? new Map()) {
    collect(tree, ns, out)
  }
  return out
}

const source = flattenLocale("en")

function tokenSignature(values: readonly string[], pattern: RegExp): string {
  const tokens = values.flatMap((value) =>
    [...value.matchAll(pattern)].map((match) => match[1])
  )
  return [...new Set(tokens)].sort().join("|")
}

/** Per-path token signatures: plain strings alone, plural groups as unions. */
function signatures(
  tree: LocaleTree,
  pattern: RegExp
): Map<string, string> {
  const out = new Map<string, string>()
  for (const [path, value] of tree.strings) {
    out.set(path, tokenSignature([value], pattern))
  }
  for (const [path, group] of tree.groups) {
    out.set(path, tokenSignature(group.values, pattern))
  }
  return out
}

describe("locale integrity across the shipped set", () => {
  it("ships exactly the registry's locale directories", () => {
    expect([...directoryOf].sort()).toEqual([...registryCodes].sort())
  })

  it("carries exactly the NAMESPACES files in every locale", () => {
    const expected = [...NAMESPACES].sort()
    for (const code of registryCodes) {
      const actual = [...(catalogues.get(code)?.keys() ?? [])].sort()
      expect(actual, `${code} namespace files`).toEqual(expected)
    }
  })

  it("preserves every {{placeholder}} the en source declares", () => {
    const enSignatures = signatures(source, PLACEHOLDER)
    for (const locale of LOCALES) {
      if (locale.code === "en") {
        continue
      }
      const localeSignatures = signatures(flattenLocale(locale.code), PLACEHOLDER)
      const drifted = [...enSignatures].filter(
        ([path, tokens]) => localeSignatures.get(path) !== tokens
      )
      expect(
        drifted.map(([path]) => `${locale.code}:${path}`),
        "paths whose {{placeholder}} set differs from en"
      ).toEqual([])
    }
  })

  it("preserves every <slot/> the en source declares", () => {
    const enSignatures = signatures(source, SLOT)
    for (const locale of LOCALES) {
      if (locale.code === "en") {
        continue
      }
      const localeSignatures = signatures(flattenLocale(locale.code), SLOT)
      const drifted = [...enSignatures].filter(
        ([path, slots]) => localeSignatures.get(path) !== slots
      )
      expect(
        drifted.map(([path]) => `${locale.code}:${path}`),
        "paths whose <slot/> set differs from en"
      ).toEqual([])
    }
  })

  it("keeps markup within the sanctioned inline vocabulary", () => {
    const offenders: string[] = []
    for (const locale of LOCALES) {
      const tree = flattenLocale(locale.code)
      const values = [
        ...[...tree.strings].map(([path, value]) => [path, value] as const),
        ...[...tree.groups].flatMap(([path, group]) =>
          group.forms.map(
            (form) => [`${path}.${form}`, group.values[group.forms.indexOf(form)]] as const
          )
        ),
      ]
      for (const [path, value] of values) {
        const stripped = value.replace(SLOT, "").replace(SANCTIONED_INLINE_TAG, "")
        if (TAG_LIKE.test(stripped)) {
          offenders.push(`${locale.code}:${path}`)
        }
      }
    }
    expect(offenders, "values carrying tags beyond the sanctioned vocabulary").toEqual(
      []
    )
  })

  it("carries its grammar's plural forms in every group", () => {
    const offenders: string[] = []
    for (const locale of LOCALES) {
      const expected = PLURAL_FORMS[locale.code]
      if (expected === undefined) {
        offenders.push(`${locale.code}: no PLURAL_FORMS row — add one`)
        continue
      }
      const forms = [...expected].sort()
      for (const [path, group] of flattenLocale(locale.code).groups) {
        if (group.forms.join("+") !== forms.join("+")) {
          offenders.push(
            `${locale.code}:${path} carries [${group.forms.join(", ")}], grammar demands [${forms.join(", ")}]`
          )
        }
      }
    }
    expect(offenders, "plural groups missing or exceeding their forms").toEqual(
      []
    )
  })

  it("names every locale with a unique non-empty endonym", () => {
    const endonyms = LOCALES.map((entry) => entry.endonym.trim())
    expect(endonyms.every((name) => name.length > 0)).toBe(true)
    expect(new Set(endonyms).size).toBe(endonyms.length)
  })
})
