/**
 * Pure tests for the overview's derivations: token-total summation
 * over message metas, the `4.1k→1.2k` format, and the
 * waiting-approval section builder. Rendering is covered by the
 * layout smoke tests; these are the logic.
 *
 * The filter overlay is driven through ink-testing-library: typing
 * letters filters the list, backspace edits the query, esc still
 * closes.
 */
import { describe, expect, it, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import {
  formatTokenTotals,
  SessionOverview,
  sessionTokenTotals,
  waitingApprovalRows,
} from "./SessionOverview"
import type { ChatBlock, Session } from "../lib/sessions"
import type { ChatMessageView } from "../lib/client"

function message(meta: ChatMessageView["meta"]): ChatMessageView {
  return {
    id: `m-${Math.random().toString(36).slice(2)}`,
    role: "assistant",
    content: "",
    toolName: null,
    parts: null,
    meta,
    createdAt: "2026-09-18T00:00:00Z",
  }
}

function block(meta: ChatMessageView["meta"]): ChatBlock {
  return { kind: "message", key: `b-${Math.random()}`, message: message(meta) }
}

const linesBlock: ChatBlock = {
  kind: "lines",
  key: "l-1",
  lines: ["one", "two"],
}

describe("sessionTokenTotals", () => {
  it("sums tokensIn/tokensOut over every message meta", () => {
    const blocks = [
      block({ tokensIn: 1200, tokensOut: 300 }),
      linesBlock,
      block({ tokensIn: 900, tokensOut: 150 }),
      block(null),
      block({ model: "lead" }),
    ]

    expect(sessionTokenTotals(blocks)).toEqual({
      tokensIn: 2100,
      tokensOut: 450,
    })
  })

  it("counts one-sided metas and marks them seen", () => {
    expect(sessionTokenTotals([block({ tokensOut: 7 })])).toEqual({
      tokensIn: 0,
      tokensOut: 7,
    })
    expect(sessionTokenTotals([block({ tokensIn: 9 })])).toEqual({
      tokensIn: 9,
      tokensOut: 0,
    })
  })

  it("is null when no message carried token numbers", () => {
    expect(sessionTokenTotals([])).toBeNull()
    expect(sessionTokenTotals([linesBlock])).toBeNull()
    expect(sessionTokenTotals([block(null), block({ model: "x" })])).toBeNull()
  })
})

describe("formatTokenTotals", () => {
  it("renders the mission shape 4.1k→1.2k", () => {
    expect(formatTokenTotals({ tokensIn: 4100, tokensOut: 1200 })).toBe(
      "4.1k→1.2k"
    )
  })

  it("keeps sub-thousand counts bare", () => {
    expect(formatTokenTotals({ tokensIn: 999, tokensOut: 42 })).toBe(
      "999→42"
    )
    expect(formatTokenTotals({ tokensIn: 0, tokensOut: 1250 })).toBe(
      "0→1.3k"
    )
  })
})

function session(overrides: Partial<Session>): Session {
  return {
    ...{
      id: `s-${Math.random().toString(36).slice(2)}`,
      name: "tab",
      status: "idle" as const,
      createdAt: Date.now(),
      unread: false,
      awaitingApproval: false,
      pendingPlan: null,
      blocks: [],
      liveText: "",
      hydrated: false,
      blocksExpanded: false,
      lastUserMessage: null,
      renamed: false,
    },
    ...overrides,
  }
}

describe("waitingApprovalRows", () => {
  it("lists only awaiting sessions, in tab order, with index and first step", () => {
    const rows = waitingApprovalRows([
      session({ name: "idle-tab" }),
      session({
        name: "fix-auth",
        awaitingApproval: true,
        pendingPlan: {
          nodes: [
            {
              key: "n1",
              profileKey: "coder",
              brief: "audit jwt flow\nthen rotate keys",
              dependsOn: [],
            },
            { key: "n2", profileKey: "coder", brief: "step 2", dependsOn: [] },
          ],
          edges: [],
        },
      }),
      session({
        name: "docs",
        awaitingApproval: true,
        pendingPlan: null,
      }),
    ])

    expect(rows).toEqual([
      { index: 1, name: "fix-auth", firstStep: "audit jwt flow" },
      { index: 2, name: "docs", firstStep: "(no plan)" },
    ])
  })

  it("is empty when nothing awaits a decision", () => {
    expect(waitingApprovalRows([session({}), session({})])).toEqual([])
    expect(waitingApprovalRows([])).toEqual([])
  })
})

function settle(ms = 80): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

describe("SessionOverview filter overlay", () => {
  test("typing letters filters by name and shows the query dim on the header", async () => {
    const { stdin, lastFrame, unmount } = render(
      <SessionOverview
        sessions={[
          session({ id: "s1", name: "Fix Auth" }),
          session({ id: "s2", name: "docs" }),
          session({ id: "s3", name: "auth-review" }),
        ]}
        activeIndex={0}
        onSelect={() => {}}
        onNewSession={() => {}}
        onClose={() => {}}
      />
    )
    await settle()
    stdin.write("a")
    await settle()
    stdin.write("u")
    await settle()
    const frame = lastFrame() ?? ""
    expect(frame).toContain("filter: au")
    expect(frame).toContain("Fix Auth")
    expect(frame).toContain("auth-review")
    expect(frame).not.toContain("docs")
    unmount()
  })

  test("backspace edits the filter; esc still closes", async () => {
    const closed: number[] = []
    const { stdin, lastFrame, unmount } = render(
      <SessionOverview
        sessions={[
          session({ id: "s1", name: "alpha" }),
          session({ id: "s2", name: "docs" }),
        ]}
        activeIndex={0}
        onSelect={() => {}}
        onNewSession={() => {}}
        onClose={() => closed.push(1)}
      />
    )
    await settle()
    stdin.write("a")
    await settle(150)
    expect(lastFrame() ?? "").toContain("filter: a")
    expect(lastFrame() ?? "").not.toContain("docs")
    stdin.write("\x7f")
    await settle()
    const afterBackspace = lastFrame() ?? ""
    expect(afterBackspace).not.toContain("filter:")
    expect(afterBackspace).toContain("alpha")
    expect(afterBackspace).toContain("docs")
    stdin.write("\x1b")
    await settle()
    expect(closed).toEqual([1])
    unmount()
  })
})
