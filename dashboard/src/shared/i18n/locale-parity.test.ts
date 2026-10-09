import { describe, expect, it } from "vitest"

import { LOCALES } from "./locales"

/**
 * Locale parity (`dashboard-i18n` D6): every shipped locale's resources
 * carry the `en` source's key set — nothing missing, nothing extra — and
 * every leaf is a non-empty string.
 *
 * Plural groups are leaf objects (`count.one` / `count.few` / …), and a
 * locale's grammar decides which forms it carries: ru answers
 * one/few/many/other, en one/other, ja and zh-CN collapse to other. The
 * group as a whole is therefore the unit of comparison, not the suffixes
 * inside it — parity is structural, and never judges translation quality
 * (D14).
 */

const PLURAL_SUFFIXES = new Set(["zero", "one", "two", "few", "many", "other"])

type Leaf = "string" | "plural"

interface FlatTree {
  keys: Map<string, Leaf>
  faults: string[]
}

interface LocaleFile {
  trees: Map<string /* ns */, Record<string, unknown>>
}

const files = import.meta.glob("./locales/*/*.json", {
  eager: true,
  import: "default",
}) as Record<string, Record<string, unknown>>

const LOCALES_BY_CODE = new Map(LOCALES.map((entry) => [entry.code, entry]))

const catalogues = new Map<string, LocaleFile>()
for (const [path, content] of Object.entries(files)) {
  const parts = path.split("/")
  const lng = parts[2] ?? ""
  const ns = (parts[3] ?? "").replace(/\.json$/, "")
  if (!LOCALES_BY_CODE.has(lng)) {
    throw new Error(`locale directory "${lng}" is not in the LOCALES registry`)
  }
  const file = catalogues.get(lng) ?? { trees: new Map() }
  file.trees.set(ns, content)
  catalogues.set(lng, file)
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value)
}

function isPluralGroup(value: unknown): value is Record<string, unknown> {
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

function flatten(
  tree: Record<string, unknown>,
  prefix: string,
  out: FlatTree
): void {
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix === "" ? key : `${prefix}.${key}`
    if (isPluralGroup(value)) {
      for (const [suffix, text] of Object.entries(value)) {
        if (typeof text === "string" && text.length === 0) {
          out.faults.push(`${path}.${suffix} is an empty translation`)
        }
      }
      out.keys.set(path, "plural")
      continue
    }
    if (typeof value === "string") {
      if (value.length === 0) {
        out.faults.push(`${path} is an empty translation`)
      }
      out.keys.set(path, "string")
      continue
    }
    if (isPlainObject(value)) {
      flatten(value, path, out)
      continue
    }
    out.faults.push(`${path} is neither a string nor a plural group`)
  }
}

function flattenLocale(file: LocaleFile): FlatTree {
  const out: FlatTree = { keys: new Map(), faults: [] }
  for (const [ns, tree] of file.trees) {
    flatten(tree, ns, out)
  }
  return out
}

const source = flattenLocale(catalogues.get("en") ?? { trees: new Map() })

describe("locale parity across the shipped set", () => {
  it("carries the en source's namespaces in every locale", () => {
    const sourceNamespaces = new Set(
      [...(catalogues.get("en")?.trees.keys() ?? [])].sort()
    )
    expect([...sourceNamespaces]).toHaveLength(23)

    for (const locale of LOCALES) {
      if (locale.code === "en") {
        continue
      }
      const file = catalogues.get(locale.code)
      const namespaces = new Set([...(file?.trees.keys() ?? [])].sort())
      expect(
        [...namespaces],
        `${locale.code} namespaces must match the en source`
      ).toEqual([...sourceNamespaces])
    }
  })

  it("holds exactly the en source's keys, with no empties", () => {
    for (const locale of LOCALES) {
      if (locale.code === "en") {
        continue
      }
      const flat = flattenLocale(
        catalogues.get(locale.code) ?? { trees: new Map() }
      )

      expect(flat.faults, `${locale.code} carries malformed leaves`).toEqual([])

      const missing = [...source.keys.keys()].filter(
        (key) => !flat.keys.has(key)
      )
      expect(
        missing,
        `${locale.code} is missing keys the en source carries`
      ).toEqual([])

      const extra = [...flat.keys.keys()].filter((key) => !source.keys.has(key))
      expect(extra, `${locale.code} carries keys the en source lacks`).toEqual(
        []
      )
    }
  })

  it("has something to compare — the en source is not empty", () => {
    expect(source.faults).toEqual([])
    expect(source.keys.size).toBeGreaterThan(0)
  })
})
