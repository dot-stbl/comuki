/**
 * Alert-card tests: 401 vs 403 titles, the framed shape (stripAnsi
 * snapshots), and the width clamp that keeps the box inside the
 * terminal. Colour assertions check that only the kind word takes
 * the status paint — body and frame stay faint / rule.
 */
import { describe, expect, it } from "bun:test"
import { ComukiApiError } from "./client"
import {
  alertFromError,
  alertLines,
  isUnrecoverableError,
  kindColor,
  renderAlertCard,
} from "./alerts"
import { colors, stripAnsi } from "../theme"

describe("alertFromError titles", () => {
  it("maps 401 to signed out and 403 to permission denied", () => {
    const signedOut = alertFromError(
      new ComukiApiError(
        401,
        "authentication.required",
        "permission 'chat:use' requires a signed-in subject"
      )
    )
    expect(signedOut.kind).toBe("error")
    expect(signedOut.title).toBe("signed out")
    expect(signedOut.code).toBe("authentication.required")

    const denied = alertFromError(
      new ComukiApiError(403, "authorization.denied", "missing chat:use")
    )
    expect(denied.title).toBe("permission denied")
    expect(denied.title).not.toBe(signedOut.title)
  })

  it("maps 5xx to server error", () => {
    const card = alertFromError(new ComukiApiError(502, "upstream", "bad gateway"))
    expect(card.title).toBe("server error")
    expect(card.detail).toBe("bad gateway")
  })

  it("maps DNS / refused to server unreachable", () => {
    const card = alertFromError(new Error("connect ECONNREFUSED 127.0.0.1:17171"))
    expect(card.title).toBe("server unreachable")
    expect(isUnrecoverableError(new Error("connect ECONNREFUSED"))).toBe(true)
    expect(
      isUnrecoverableError(new ComukiApiError(401, "authentication.required", "no"))
    ).toBe(false)
  })
})

describe("alertLines slab", () => {
  it("draws the 401 slab with explicit status and hints", () => {
    const lines = alertLines(
      new ComukiApiError(
        401,
        "authentication.required",
        "permission 'chat:use' requires a signed-in subject"
      )
    )
    const plain = lines.map(stripAnsi)
    expect(plain[0]).toContain("[error] authentication.required")
    expect(plain.some((line) => line.includes("permission 'chat:use'"))).toBe(
      true
    )
    expect(plain.some((line) => line.includes("/login"))).toBe(true)
    expect(plain.some((line) => line.includes("/retry"))).toBe(true)
  })

  it("paints only the kind word with the error colour", () => {
    const lines = renderAlertCard({
      kind: "error",
      title: "signed out",
      detail: "no cookie",
      hints: [],
    })
    expect(lines[0]).toContain(kindColor("error"))
    expect(lines[1]).toContain(colors.faint)
    expect(lines[1]).not.toContain(colors.error)
  })

  it("clamps the slab to the terminal width", () => {
    const lines = alertLines(
      new ComukiApiError(401, "authentication.required", "x".repeat(200)),
      40
    )
    for (const line of lines) {
      expect(stripAnsi(line).length).toBeLessThanOrEqual(40)
    }
  })

  it("uses the 403 title on the status row when no code is present", () => {
    const plain = alertLines(new ComukiApiError(403, undefined, "nope")).map(
      stripAnsi
    )
    expect(plain[0]).toContain("[error] permission denied")
    expect(plain[0]).not.toContain("signed out")
  })
})
