import { describe, expect, it } from "vitest"

import {
  landingFor,
  parseLoginSearch,
  safeRedirect,
  signInTarget,
} from "./landing"

describe("the three landings", () => {
  it("says nothing happened when nothing did", () => {
    const cold = landingFor()

    expect(cold.kind).toBe("cold")
    expect(cold.notice).toBeNull()
    expect(cold.lead).toMatch(/Sign in to reach/)
  })

  it("says so when the session expired", () => {
    const expired = landingFor("expired")

    expect(expired.kind).toBe("expired")
    expect(expired.notice).toBe("Your session expired")
    expect(expired.lead).toMatch(/pick up where you left off/)
  })

  // The difference that matters: a chosen departure is not an incident, and the
  // copy must not tell someone who pressed Sign out that something went wrong.
  it("confirms a departure without alarming about it", () => {
    const out = landingFor("signed-out")

    expect(out.kind).toBe("signed-out")
    expect(out.notice).toBe("You're signed out")
    expect(out.notice).not.toMatch(/expired/i)
  })

  it("gives each arrival its own words", () => {
    const notices = new Set(
      [landingFor(), landingFor("expired"), landingFor("signed-out")].map(
        (copy) => copy.lead
      )
    )

    expect(notices.size).toBe(3)
  })
})

describe("the login search", () => {
  it("reads the arrival and the return path", () => {
    expect(parseLoginSearch({ reason: "expired", redirect: "/runs/r_1" })).toEqual(
      { reason: "expired", redirect: "/runs/r_1" }
    )
  })

  it("drops an arrival it does not recognise, rather than inventing one", () => {
    expect(parseLoginSearch({ reason: "kicked" })).toEqual({})
    expect(parseLoginSearch({})).toEqual({})
  })

  it("keeps a search string on the return path", () => {
    expect(parseLoginSearch({ redirect: "/runs?status=waiting" }).redirect).toBe(
      "/runs?status=waiting"
    )
  })
})

// The value comes out of the address bar, so it is attacker-supplied by
// definition: a sign-in screen that will navigate anywhere it is told is a
// phishing hop with the product's own domain in front of it.
describe("the return path", () => {
  it("accepts an in-app absolute path", () => {
    expect(safeRedirect("/queue")).toBe("/queue")
  })

  it.each([
    ["an absolute URL", "https://evil.test/steal"],
    ["a protocol-relative URL", "//evil.test/steal"],
    ["a backslash escape", "/\\evil.test"],
    ["a bare relative path", "runs"],
    ["an empty string", ""],
    ["a non-string", 7],
  ])("refuses %s", (_label, value) => {
    expect(safeRedirect(value)).toBeUndefined()
  })

  it("falls back to the board when there is nowhere to return to", () => {
    expect(signInTarget()).toBe("/")
    expect(signInTarget("https://evil.test")).toBe("/")
    expect(signInTarget("/cost")).toBe("/cost")
  })
})
