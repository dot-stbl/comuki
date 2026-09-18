import { describe, expect, it } from "bun:test"
import { colors, paint } from "../theme"
import {
  DISABLE_MOUSE,
  ENABLE_MOUSE,
  hitTestApproval,
  hitTestTab,
  isSgrMouseChunk,
  parseSgrMouse,
  resolveMouseClick,
  tabCellWidth,
  tabHitSegments,
  type MouseLayout,
} from "./mouse"

describe("parseSgrMouse", () => {
  it("parses a left-button press at 1-based cells", () => {
    expect(parseSgrMouse("\x1b[<0;12;4M")).toEqual({
      button: 0,
      x: 12,
      y: 4,
      press: true,
    })
  })

  it("parses a release (`m` terminator)", () => {
    expect(parseSgrMouse("\x1b[<0;12;4m")).toEqual({
      button: 0,
      x: 12,
      y: 4,
      press: false,
    })
  })

  it("parses a right-button and a motion-encoded button", () => {
    expect(parseSgrMouse("\x1b[<2;1;1M")?.button).toBe(2)
    expect(parseSgrMouse("\x1b[<32;8;3M")?.button).toBe(32)
  })

  it("returns null for anything that is not an SGR mouse report", () => {
    expect(parseSgrMouse("")).toBeNull()
    expect(parseSgrMouse("a")).toBeNull()
    expect(parseSgrMouse("\x1b[H")).toBeNull()
    expect(parseSgrMouse("\x1b[<0;12;4")).toBeNull()
    expect(parseSgrMouse("\x1b[0;12;4M")).toBeNull()
  })

  it("rejects non-positive coordinates", () => {
    expect(parseSgrMouse("\x1b[<0;0;1M")).toBeNull()
    expect(parseSgrMouse("\x1b[<0;1;0M")).toBeNull()
  })

  it("still parses when extra bytes trail the terminator", () => {
    expect(parseSgrMouse("\x1b[<0;3;1Ma")).toEqual({
      button: 0,
      x: 3,
      y: 1,
      press: true,
    })
  })

  it("parses the CSI body after Ink strips the leading ESC", () => {
    expect(parseSgrMouse("[<0;12;4M")).toEqual({
      button: 0,
      x: 12,
      y: 4,
      press: true,
    })
  })
})

describe("isSgrMouseChunk", () => {
  it("is true for full and ESC-stripped reports, false otherwise", () => {
    expect(isSgrMouseChunk("\x1b[<0;1;1M")).toBe(true)
    expect(isSgrMouseChunk("[<0;1;1m")).toBe(true)
    expect(isSgrMouseChunk("a")).toBe(false)
    expect(isSgrMouseChunk("\x1b[H")).toBe(false)
  })
})

describe("ENABLE_MOUSE / DISABLE_MOUSE", () => {
  it("are the DECSET 1000 + 1006 + 1002 pair", () => {
    expect(ENABLE_MOUSE).toBe("\x1b[?1000h\x1b[?1006h\x1b[?1002h")
    expect(DISABLE_MOUSE).toBe("\x1b[?1000l\x1b[?1006l\x1b[?1002l")
  })
})

describe("tabHitSegments / hitTestTab", () => {
  const sessions = [
    { name: "alpha", awaitingApproval: false, unread: false },
    { name: "beta", awaitingApproval: true, unread: false },
    { name: "gamma", awaitingApproval: false, unread: true },
  ]

  it("lays tabs out after the two-space indent, one-space gutters", () => {
    const segments = tabHitSegments(sessions)
    expect(segments).toHaveLength(3)
    // indent 2 → first cell is 3. `[1] alpha` is 9 cells → [3, 12).
    expect(segments[0]).toEqual({ index: 0, start: 3, end: 12 })
    // gutter at 12, then `[2] beta !` (10 cells) → [13, 23).
    const betaWidth = tabCellWidth(1, sessions[1]!)
    expect(segments[1]).toEqual({
      index: 1,
      start: 13,
      end: 13 + betaWidth,
    })
  })

  it("maps an x inside a tab to its index and misses the gutters / [+]", () => {
    expect(hitTestTab(3, sessions)).toBe(0)
    expect(hitTestTab(11, sessions)).toBe(0)
    expect(hitTestTab(1, sessions)).toBeNull()
    expect(hitTestTab(2, sessions)).toBeNull()
    const last = tabHitSegments(sessions).at(-1)
    expect(last).toBeDefined()
    expect(hitTestTab(last!.end, sessions)).toBeNull()
    expect(hitTestTab(last!.end - 1, sessions)).toBe(2)
  })

  it("caps at 9 tabs, matching TabBar's slice", () => {
    const many = Array.from({ length: 12 }, (_, index) => ({
      name: `s${index}`,
      awaitingApproval: false,
      unread: false,
    }))
    expect(tabHitSegments(many)).toHaveLength(9)
    expect(hitTestTab(3, many)).toBe(0)
  })
})

describe("hitTestApproval", () => {
  const hint = `  ${paint("approve", colors.ok)}${paint(" · ", colors.dim)}${paint(
    "reject",
    colors.error
  )}${paint(" [reason]", colors.dim)}`

  it("hits approve / reject by the word under x", () => {
    // Visible: `  approve · reject [reason]`
    // cells  0123456789…
    expect(hitTestApproval(hint, 3)).toBe("approve") // first letter
    expect(hitTestApproval(hint, 9)).toBe("approve") // last letter of approve
    expect(hitTestApproval(hint, 13)).toBe("reject")
    expect(hitTestApproval(hint, 18)).toBe("reject")
  })

  it("picks the closer word on the gutter between them", () => {
    expect(hitTestApproval(hint, 10)).toBe("approve")
    expect(hitTestApproval(hint, 12)).toBe("reject")
  })

  it("returns null when the line has neither word", () => {
    expect(hitTestApproval("  plan · 3 steps", 4)).toBeNull()
    expect(hitTestApproval("", 1)).toBeNull()
  })

  it("still matches a line that only contains one of the words", () => {
    expect(hitTestApproval("please approve this", 8)).toBe("approve")
    expect(hitTestApproval("reject the plan", 1)).toBe("reject")
  })
})

describe("resolveMouseClick", () => {
  const sessions = [
    { name: "alpha", awaitingApproval: false, unread: false },
    { name: "beta", awaitingApproval: true, unread: false },
  ]
  const layout: MouseLayout = {
    tabRow: 2,
    sessions,
    transcriptTop: 3,
    hasHint: false,
    visibleLines: [hintLine()],
    awaitingApproval: true,
  }

  it("hits a tab on the tab row and ignores the status line", () => {
    expect(resolveMouseClick({ x: 3, y: 2 }, layout)).toEqual({
      kind: "tab",
      index: 0,
    })
    expect(resolveMouseClick({ x: 1, y: 1 }, layout)).toEqual({ kind: "none" })
  })

  it("hits approve / reject on the visible transcript line", () => {
    expect(resolveMouseClick({ x: 3, y: 3 }, layout)).toEqual({
      kind: "approve",
    })
    expect(resolveMouseClick({ x: 13, y: 3 }, layout)).toEqual({
      kind: "reject",
    })
  })

  it("skips transcript clicks when not awaiting approval", () => {
    expect(
      resolveMouseClick(
        { x: 3, y: 3 },
        { ...layout, awaitingApproval: false }
      )
    ).toEqual({ kind: "none" })
  })

  it("accounts for the expand-hint row above the window", () => {
    const withHint: MouseLayout = { ...layout, hasHint: true }
    // localY 0 is the hint; the approve line is the next row.
    expect(resolveMouseClick({ x: 3, y: 3 }, withHint)).toEqual({
      kind: "none",
    })
    expect(resolveMouseClick({ x: 3, y: 4 }, withHint)).toEqual({
      kind: "approve",
    })
  })
})

function hintLine(): string {
  return `  ${paint("approve", colors.ok)}${paint(" · ", colors.dim)}${paint(
    "reject",
    colors.error
  )}${paint(" [reason]", colors.dim)}`
}
