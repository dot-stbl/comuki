/**
 * Pure tests for the overview's derivations: token-total summation
 * over message metas, the `4.1k→1.2k` format, and the
 * waiting-approval section builder. Rendering is covered by the
 * layout smoke tests; these are the logic.
 */
import { describe, expect, it } from "bun:test"
import {
  formatTokenTotals,
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
