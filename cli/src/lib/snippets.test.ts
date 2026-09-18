import { describe, expect, it } from "bun:test"
import { rm } from "node:fs/promises"
import {
  SNIPPET_NAME_PATTERN,
  getSnippet,
  isValidSnippetName,
  readSnippetsFile,
  removeSnippet,
  saveSnippet,
  snippetListingLines,
  snippetNames,
  snippetPreview,
  writeSnippetsFile,
  type PersistedSnippets,
} from "./snippets"
import { stripAnsi } from "../theme"

const store = (entries: Record<string, string>): PersistedSnippets => ({
  snippets: entries,
})

describe("isValidSnippetName", () => {
  it("accepts lowercase letters, digits and dashes", () => {
    expect(isValidSnippetName("deploy")).toBe(true)
    expect(isValidSnippetName("fix-readme-2")).toBe(true)
    expect(isValidSnippetName("a")).toBe(true)
    expect(isValidSnippetName("42")).toBe(true)
  })

  it("rejects uppercase, spaces, underscores, dots and empty strings", () => {
    expect(isValidSnippetName("Deploy")).toBe(false)
    expect(isValidSnippetName("fix readme")).toBe(false)
    expect(isValidSnippetName("fix_readme")).toBe(false)
    expect(isValidSnippetName("fix.readme")).toBe(false)
    expect(isValidSnippetName("")).toBe(false)
    expect(SNIPPET_NAME_PATTERN.test("-")).toBe(true)
  })
})

describe("saveSnippet", () => {
  it("stores a new snippet and overwrites an existing name", () => {
    const once = saveSnippet(store({}), "deploy", "ship it")
    expect(getSnippet(once, "deploy")).toBe("ship it")
    const twice = saveSnippet(once, "deploy", "ship it now")
    expect(getSnippet(twice, "deploy")).toBe("ship it now")
    expect(snippetNames(twice)).toEqual(["deploy"])
  })

  it("trims surrounding whitespace from the text", () => {
    const saved = saveSnippet(store({}), "deploy", "  ship it\n")
    expect(getSnippet(saved, "deploy")).toBe("ship it")
  })

  it("leaves the store unchanged for an invalid name or blank text", () => {
    const base = store({ keep: "kept" })
    expect(saveSnippet(base, "Bad Name", "text")).toBe(base)
    expect(saveSnippet(base, "UPPER", "text")).toBe(base)
    expect(saveSnippet(base, "blank", "   ")).toBe(base)
    expect(saveSnippet(base, "blank", "")).toBe(base)
  })
})

describe("removeSnippet", () => {
  it("removes a stored name and its listing entry", () => {
    const base = store({ deploy: "ship", audit: "check" })
    const next = removeSnippet(base, "deploy")
    expect(getSnippet(next, "deploy")).toBeUndefined()
    expect(snippetNames(next)).toEqual(["audit"])
  })

  it("leaves the store unchanged for an unknown name", () => {
    const base = store({ deploy: "ship" })
    expect(removeSnippet(base, "nope")).toBe(base)
  })
})

describe("snippetNames", () => {
  it("sorts names alphabetically regardless of insertion order", () => {
    const names = snippetNames(
      store({ zebra: "z", alpha: "a", mid: "m" })
    )
    expect(names).toEqual(["alpha", "mid", "zebra"])
  })
})

describe("snippetPreview", () => {
  it("collapses whitespace and truncates at 48 chars", () => {
    expect(snippetPreview("fix   the\n\treadme")).toBe("fix the readme")
    const long = "x".repeat(60)
    expect(snippetPreview(long)).toBe(`${"x".repeat(48)}…`)
  })
})

describe("snippetListingLines", () => {
  it("explains the save flow when nothing is stored", () => {
    const lines = snippetListingLines(store({})).map(stripAnsi)
    expect(lines).toHaveLength(1)
    expect(lines[0]).toContain("no saved snippets")
    expect(lines[0]).toContain("/snip save <name>")
  })

  it("lists names with a preview column", () => {
    const lines = snippetListingLines(
      store({ deploy: "ship it", audit: "check the logs" })
    ).map(stripAnsi)
    expect(lines[0]).toContain("snippets")
    expect(lines[1]).toContain("audit")
    expect(lines[1]).toContain("check the logs")
    expect(lines[2]).toContain("deploy")
    expect(lines[2]).toContain("ship it")
  })
})

describe("snippets file round-trip", () => {
  const path = `${import.meta.dir}/snippets-roundtrip.tmp.json`

  it("persists save/remove through snippets.json", async () => {
    const saved = saveSnippet(store({}), "deploy", "ship it")
    await writeSnippetsFile(saved, path)
    expect(getSnippet(await readSnippetsFile(path), "deploy")).toBe("ship it")

    const removed = removeSnippet(await readSnippetsFile(path), "deploy")
    await writeSnippetsFile(removed, path)
    expect(getSnippet(await readSnippetsFile(path), "deploy")).toBeUndefined()
    await rm(path, { force: true })
  })

  it("reads a missing file as an empty store", async () => {
    const empty = await readSnippetsFile(`${import.meta.dir}/nope.json`)
    expect(empty).toEqual({ snippets: {} })
  })

  it("reads a malformed or foreign-shaped file as an empty store", async () => {
    const malformed = `${import.meta.dir}/snippets-malformed.tmp.json`
    await Bun.write(malformed, "{ not json")
    expect(await readSnippetsFile(malformed)).toEqual({ snippets: {} })
    await Bun.write(malformed, JSON.stringify({ other: true }))
    expect(await readSnippetsFile(malformed)).toEqual({ snippets: {} })
    await rm(malformed, { force: true })
  })

  it("drops non-string entries from a foreign file", async () => {
    const foreign = `${import.meta.dir}/snippets-foreign.tmp.json`
    await Bun.write(
      foreign,
      JSON.stringify({ snippets: { ok: "yes", bad: 7, worse: null } })
    )
    const restored = await readSnippetsFile(foreign)
    expect(restored.snippets).toEqual({ ok: "yes" })
    await rm(foreign, { force: true })
  })
})
