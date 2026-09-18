import { describe, expect, it } from "bun:test"
import {
  isWellKnownProfile,
  profileListingLines,
  profileStoredLine,
  resolveProfile,
  WELL_KNOWN_PROFILES,
} from "./profiles"
import { stripAnsi } from "../theme"
import { symbols } from "../theme"
import type { ProfileView } from "./client"

function profile(
  key: string,
  name: string,
  description: string
): ProfileView {
  return { key, name, description }
}

const catalog = [
  profile("implement", "Implementer", "writes the code"),
  profile("explore-readonly", "Explorer", "read-only reconnaissance"),
]

describe("WELL_KNOWN_PROFILES", () => {
  it("lists the four control-plane stems", () => {
    expect(WELL_KNOWN_PROFILES).toEqual([
      "implement",
      "explore-readonly",
      "docs-writer",
      "pr-review",
    ])
  })
})

describe("isWellKnownProfile", () => {
  it("accepts the four stems and rejects everything else", () => {
    expect(isWellKnownProfile("implement")).toBe(true)
    expect(isWellKnownProfile("pr-review")).toBe(true)
    expect(isWellKnownProfile("ghost")).toBe(false)
    expect(isWellKnownProfile("")).toBe(false)
  })
})

describe("resolveProfile", () => {
  it("matches catalog key then name, case-insensitive", () => {
    expect(resolveProfile("implement", catalog)).toBe("implement")
    expect(resolveProfile("IMPLEMENT", catalog)).toBe("implement")
    expect(resolveProfile("Explorer", catalog)).toBe("explore-readonly")
  })

  it("falls back to well-known stems when the catalog misses", () => {
    expect(resolveProfile("docs-writer", [])).toBe("docs-writer")
    expect(resolveProfile("pr-review", catalog)).toBe("pr-review")
  })

  it("returns undefined on an empty query or a miss", () => {
    expect(resolveProfile("", catalog)).toBeUndefined()
    expect(resolveProfile("  ", catalog)).toBeUndefined()
    expect(resolveProfile("ghost", catalog)).toBeUndefined()
  })
})

describe("profileListingLines", () => {
  it("lists the current preference and every catalog profile", () => {
    const plain = profileListingLines("implement", catalog).map(stripAnsi)
    expect(plain[0]).toContain("profile · preferred: implement")
    expect(plain[1]).toContain("implement — writes the code")
    expect(plain[2]).toContain("explore-readonly — read-only reconnaissance")
    expect(plain[plain.length - 1]).toContain("createSession has no profile field")
  })

  it("falls back to well-known names when the host catalog is empty", () => {
    const plain = profileListingLines(null, [], { fromHost: false }).map(
      stripAnsi
    )
    expect(plain[0]).toContain("preferred: none")
    expect(plain[1]).toContain("host catalog unavailable")
    expect(plain[2]).toContain("implement")
    expect(plain[3]).toContain("explore-readonly")
    expect(plain[4]).toContain("docs-writer")
    expect(plain[5]).toContain("pr-review")
  })
})

describe("profileStoredLine", () => {
  it("renders the dim local-preference notice", () => {
    const line = profileStoredLine("implement")
    expect(stripAnsi(line)).toBe(
      `  ${symbols.event} profile ${symbols.arrow} implement (local preference — sessions ignore it)`
    )
  })
})
