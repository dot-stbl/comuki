import { describe, expect, it } from "bun:test"
import { rm } from "node:fs/promises"
import {
  ALIAS_NAME_PATTERN,
  aliasListingLines,
  aliasNames,
  aliasPreview,
  expandAlias,
  getAlias,
  isValidAliasName,
  readAliasesFile,
  removeAlias,
  setAlias,
  writeAliasesFile,
  type AliasStore,
} from "./aliases"
import { stripAnsi } from "../theme"

const store = (entries: Record<string, string>): AliasStore => entries

describe("isValidAliasName", () => {
  it("accepts a letter then lowercase letters, digits and dashes", () => {
    expect(isValidAliasName("fix")).toBe(true)
    expect(isValidAliasName("rv")).toBe(true)
    expect(isValidAliasName("fix-tests-2")).toBe(true)
    expect(isValidAliasName("a")).toBe(true)
  })

  it("rejects digits-first, uppercase, spaces, underscores and empty", () => {
    expect(isValidAliasName("42")).toBe(false)
    expect(isValidAliasName("Fix")).toBe(false)
    expect(isValidAliasName("fix tests")).toBe(false)
    expect(isValidAliasName("fix_tests")).toBe(false)
    expect(isValidAliasName("")).toBe(false)
    expect(ALIAS_NAME_PATTERN.test("-")).toBe(false)
  })
})

describe("setAlias / removeAlias", () => {
  it("stores a new alias and overwrites an existing name", () => {
    const once = setAlias(store({}), "fix", "please fix the failing tests")
    expect(getAlias(once, "fix")).toBe("please fix the failing tests")
    const twice = setAlias(once, "fix", "please fix them now")
    expect(getAlias(twice, "fix")).toBe("please fix them now")
    expect(aliasNames(twice)).toEqual(["fix"])
  })

  it("trims surrounding whitespace from the text", () => {
    const saved = setAlias(store({}), "rv", "  review this diff\n")
    expect(getAlias(saved, "rv")).toBe("review this diff")
  })

  it("leaves the store unchanged for an invalid name or blank text", () => {
    const base = store({ keep: "kept" })
    expect(setAlias(base, "Bad", "text")).toBe(base)
    expect(setAlias(base, "42", "text")).toBe(base)
    expect(setAlias(base, "blank", "   ")).toBe(base)
    expect(setAlias(base, "blank", "")).toBe(base)
  })

  it("removes a stored name; unknown names leave the store unchanged", () => {
    const base = store({ fix: "ship", rv: "check" })
    const next = removeAlias(base, "fix")
    expect(getAlias(next, "fix")).toBeUndefined()
    expect(aliasNames(next)).toEqual(["rv"])
    expect(removeAlias(base, "nope")).toBe(base)
  })
})

describe("aliasNames / aliasPreview / listing", () => {
  it("sorts names alphabetically regardless of insertion order", () => {
    expect(aliasNames(store({ zebra: "z", alpha: "a", mid: "m" }))).toEqual([
      "alpha",
      "mid",
      "zebra",
    ])
  })

  it("collapses whitespace and truncates at 48 chars", () => {
    expect(aliasPreview("fix   the\n\ttests")).toBe("fix the tests")
    expect(aliasPreview("x".repeat(60))).toBe(`${"x".repeat(48)}…`)
  })

  it("explains the set flow when nothing is stored", () => {
    const lines = aliasListingLines(store({})).map(stripAnsi)
    expect(lines).toHaveLength(1)
    expect(lines[0]).toContain("no aliases")
    expect(lines[0]).toContain("/alias set")
  })

  it("lists names with a preview column", () => {
    const lines = aliasListingLines(
      store({ fix: "please fix the failing tests", rv: "review this diff" })
    ).map(stripAnsi)
    expect(lines[0]).toContain("aliases")
    expect(lines[1]).toContain("fix")
    expect(lines[1]).toContain("please fix the failing tests")
    expect(lines[2]).toContain("rv")
    expect(lines[2]).toContain("review this diff")
  })
})

describe("expandAlias vs slash precedence", () => {
  const aliases = store({
    fix: "please fix the failing tests",
    rv: "review this diff",
    retry: "this must never fire",
  })

  it("expands a bare alias name that is the entire prompt", () => {
    expect(expandAlias(aliases, "fix")).toBe("please fix the failing tests")
    expect(expandAlias(aliases, "  rv  ")).toBe("review this diff")
  })

  it("expands /name when it is not a registered slash command", () => {
    expect(expandAlias(aliases, "/fix")).toBe("please fix the failing tests")
    expect(expandAlias(aliases, "/rv")).toBe("review this diff")
  })

  it("slash commands always win — even when the store has the same name", () => {
    expect(expandAlias(aliases, "/retry")).toBeNull()
    expect(expandAlias(aliases, "retry")).toBeNull()
    expect(expandAlias(aliases, "/help")).toBeNull()
    expect(expandAlias(aliases, "/q")).toBeNull()
  })

  it("does not expand a name that is only a prefix of the prompt", () => {
    expect(expandAlias(aliases, "fix the tests")).toBeNull()
    expect(expandAlias(aliases, "/fix now")).toBeNull()
  })

  it("returns null for unknown names and empty input", () => {
    expect(expandAlias(aliases, "nope")).toBeNull()
    expect(expandAlias(aliases, "/nope")).toBeNull()
    expect(expandAlias(aliases, "")).toBeNull()
    expect(expandAlias(store({}), "fix")).toBeNull()
  })
})

describe("aliases file round-trip", () => {
  const path = `${import.meta.dir}/aliases-roundtrip.tmp.json`

  it("persists set/remove through aliases.json", async () => {
    const saved = setAlias(store({}), "fix", "please fix the failing tests")
    await writeAliasesFile(saved, path)
    expect(getAlias(await readAliasesFile(path), "fix")).toBe(
      "please fix the failing tests"
    )

    const removed = removeAlias(await readAliasesFile(path), "fix")
    await writeAliasesFile(removed, path)
    expect(getAlias(await readAliasesFile(path), "fix")).toBeUndefined()
    await rm(path, { force: true })
  })

  it("reads a missing, malformed or array file as an empty store", async () => {
    expect(await readAliasesFile(`${import.meta.dir}/nope-aliases.json`)).toEqual(
      {}
    )
    const malformed = `${import.meta.dir}/aliases-malformed.tmp.json`
    await Bun.write(malformed, "{ not json")
    expect(await readAliasesFile(malformed)).toEqual({})
    await Bun.write(malformed, JSON.stringify(["fix"]))
    expect(await readAliasesFile(malformed)).toEqual({})
    await rm(malformed, { force: true })
  })

  it("drops non-string entries from a foreign file", async () => {
    const foreign = `${import.meta.dir}/aliases-foreign.tmp.json`
    await Bun.write(foreign, JSON.stringify({ ok: "yes", bad: 7, worse: null }))
    expect(await readAliasesFile(foreign)).toEqual({ ok: "yes" })
    await rm(foreign, { force: true })
  })
})
