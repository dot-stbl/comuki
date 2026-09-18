import { describe, expect, it } from "bun:test"
import {
  FOOTER_EXPAND_HINT,
  footerActions,
  footerHintRow,
  footerRowCount,
  footerTopRow,
  hitTestFooterAction,
  isCollapsedFooterClick,
  isFooterExpandChord,
  wrapActionIndex,
  type FooterActionState,
} from "./footer-actions"

const IDLE: FooterActionState = {
  thinking: false,
  awaitingApproval: false,
  signedOut: false,
  sessionCount: 1,
}

function ids(state: FooterActionState): readonly string[] {
  return footerActions(state).map((action) => action.id)
}

describe("footerActions", () => {
  it("always includes the core set plus help and quit", () => {
    expect(ids(IDLE)).toEqual([
      "new",
      "overview",
      "verbose",
      "copy",
      "search",
      "help",
      "quit",
    ])
  })

  it("carries chord hints on the always-on items", () => {
    const actions = footerActions(IDLE)
    expect(actions[0]).toEqual({
      id: "new",
      label: "new session",
      hint: "ctrl+n",
    })
    expect(actions[1]?.hint).toBe("esc")
    expect(actions[2]?.hint).toBe("ctrl+o")
    expect(actions[3]?.hint).toBe("ctrl+y")
    expect(actions[4]?.hint).toBe("ctrl+f")
    expect(actions.at(-1)?.hint).toBeUndefined()
  })

  it("inserts login when signed out", () => {
    expect(ids({ ...IDLE, signedOut: true })).toEqual([
      "new",
      "overview",
      "verbose",
      "copy",
      "search",
      "login",
      "help",
      "quit",
    ])
  })

  it("inserts approve and reject only while awaiting approval", () => {
    expect(ids({ ...IDLE, awaitingApproval: true })).toEqual([
      "new",
      "overview",
      "verbose",
      "copy",
      "search",
      "approve",
      "reject",
      "help",
      "quit",
    ])
    expect(ids(IDLE)).not.toContain("approve")
    expect(ids(IDLE)).not.toContain("reject")
  })

  it("inserts stop only while thinking", () => {
    expect(ids({ ...IDLE, thinking: true })).toContain("stop")
    expect(ids(IDLE)).not.toContain("stop")
  })

  it("stacks every gated item when all flags are on", () => {
    expect(
      ids({
        thinking: true,
        awaitingApproval: true,
        signedOut: true,
        sessionCount: 3,
      })
    ).toEqual([
      "new",
      "overview",
      "verbose",
      "copy",
      "search",
      "login",
      "approve",
      "reject",
      "stop",
      "help",
      "quit",
    ])
  })

  it("keeps new session even with zero open tabs", () => {
    expect(ids({ ...IDLE, sessionCount: 0 })).toContain("new")
  })
})

describe("wrapActionIndex", () => {
  it("wraps forward and backward", () => {
    expect(wrapActionIndex(0, 7, 1)).toBe(1)
    expect(wrapActionIndex(6, 7, 1)).toBe(0)
    expect(wrapActionIndex(0, 7, -1)).toBe(6)
    expect(wrapActionIndex(3, 7, -1)).toBe(2)
  })

  it("stays at 0 on an empty list", () => {
    expect(wrapActionIndex(0, 0, 1)).toBe(0)
    expect(wrapActionIndex(4, 0, -1)).toBe(0)
  })
})

describe("footerRowCount", () => {
  it("is one row when collapsed", () => {
    expect(footerRowCount(false, 7)).toBe(1)
    expect(footerRowCount(false, 0)).toBe(1)
  })

  it("is rule + actions + the collapsed badge row when expanded", () => {
    expect(footerRowCount(true, 7)).toBe(9)
    expect(footerRowCount(true, 11)).toBe(13)
    expect(footerRowCount(true, 0)).toBe(2)
  })
})

describe("hitTestFooterAction", () => {
  it("maps clicks under the dim rule onto action indices", () => {
    // footerTopY = 20 (rule); actions occupy 21..27
    expect(hitTestFooterAction(21, 20, 7)).toBe(0)
    expect(hitTestFooterAction(24, 20, 7)).toBe(3)
    expect(hitTestFooterAction(27, 20, 7)).toBe(6)
  })

  it("misses the rule itself and rows past the list", () => {
    expect(hitTestFooterAction(20, 20, 7)).toBeNull()
    expect(hitTestFooterAction(28, 20, 7)).toBeNull()
    expect(hitTestFooterAction(19, 20, 7)).toBeNull()
  })

  it("returns null when there are no actions", () => {
    expect(hitTestFooterAction(21, 20, 0)).toBeNull()
  })
})

describe("footerTopRow", () => {
  it("sits after status + tab + viewport", () => {
    expect(
      footerTopRow({ hasTabBar: true, viewportHeight: 20, searchOpen: false })
    ).toBe(23)
    expect(
      footerTopRow({ hasTabBar: false, viewportHeight: 20, searchOpen: false })
    ).toBe(22)
  })

  it("accounts for the search row", () => {
    expect(
      footerTopRow({ hasTabBar: true, viewportHeight: 19, searchOpen: true })
    ).toBe(23)
  })
})

describe("footerHintRow", () => {
  it("is the badge row under the rule and the action list", () => {
    expect(footerHintRow(20, 7)).toBe(28)
    expect(footerHintRow(20, 0)).toBe(21)
  })
})

describe("isCollapsedFooterClick", () => {
  it("is true only on the collapsed footer row", () => {
    expect(isCollapsedFooterClick(23, 23, false)).toBe(true)
    expect(isCollapsedFooterClick(23, 23, true)).toBe(false)
    expect(isCollapsedFooterClick(22, 23, false)).toBe(false)
  })
})

describe("FOOTER_EXPAND_HINT", () => {
  it("names the expand chord", () => {
    expect(FOOTER_EXPAND_HINT).toBe("ctrl+/ actions")
  })
})

describe("isFooterExpandChord", () => {
  it("matches ctrl+/ and the C0 fallbacks", () => {
    expect(isFooterExpandChord("/", { ctrl: true })).toBe(true)
    expect(isFooterExpandChord("?", { ctrl: true })).toBe(true)
    expect(isFooterExpandChord("\x1f", { ctrl: true })).toBe(true)
  })

  it("ignores the same bytes without ctrl, and other ctrl letters", () => {
    expect(isFooterExpandChord("/", { ctrl: false })).toBe(false)
    expect(isFooterExpandChord("n", { ctrl: true })).toBe(false)
    expect(isFooterExpandChord("", { ctrl: true })).toBe(false)
  })
})
