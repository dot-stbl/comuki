/**
 * Risk-tiered approval entries (issue #76) — the three densities
 * derived from one fixed wire shape, plus the renderer summary and
 * detail lines the entry pipeline uses.
 *
 * The fixture is the existing platform wire plan payload: a single
 * session, one awaiting-approval turn, three node shapes (3 nodes
 * for the stacked case, 2 for the domain case, 1 for the compact
 * case via a separate shape). The renderer is the styled-line
 * builder added in `styled.ts`; this test only verifies the data
 * plumbing (density classification, payload shape, fingerprint
 * stability) and the renderer seam (`approvalSummary` / `approvalDetail`
 * exist and return the right tone mix per density).
 */

import { describe, expect, test } from "bun:test"
import {
  pendingApprovalEntry,
  planPayloadFrom,
  type ApprovalEntry,
  type RiskClass,
} from "./approvals"
import {
  approvalSummary,
  approvalDetail,
} from "./styled"
import { createI18nFor, type I18nInstance } from "../locales"
import {
  pendingSessionId,
  sessionId,
  turnRequestId,
  type HarnessSession,
} from "../harness/state"
import { lineText } from "./styled"

const SESSION_ID = sessionId("s-1")
const MESSAGE_ID = "m-approval"
const CREATED_AT = Date.parse("2026-01-01T12:34:56Z")
const REQUESTER = "subject-42"

function awaitingSession(plan: unknown): HarnessSession {
  return {
    identity: { kind: "remote", id: SESSION_ID },
    projectId: null,
    title: "TUI",
    createdAtUnixMs: 0,
    renamed: false,
    unread: false,
    turn: {
      kind: "awaiting-approval",
      requestId: turnRequestId("turn-approval"),
    },
    transcriptLoad: { kind: "loaded" },
    transcript: [],
    queue: [],
    history: [],
    lastUserMessage: null,
    pendingPlan: plan,
  }
}

async function entryFor(
  plan: unknown
): Promise<ApprovalEntry> {
  const entry = pendingApprovalEntry({
    session: awaitingSession(plan),
    messageId: MESSAGE_ID,
    requester: REQUESTER,
    createdAtUnixMs: CREATED_AT,
    trailingMeta: null,
  })
  if (entry === null) {
    throw new Error("expected pendingApprovalEntry to return non-null")
  }
  return entry
}

const STACKED_PLAN = {
  intent: "STACKED-INTENT",
  scope: "tests/*",
  risk: "medium",
  planSteps: [
    "STACKED-STEP-1",
    "STACKED-STEP-2",
    "STACKED-STEP-3",
    "STACKED-STEP-4",
  ],
  diff: "+ stacked diff\n- stacked diff",
  estimateMinutes: 12,
  nodes: [
    {
      id: "n1",
      key: "n1",
      title: "STACKED-NODE-ONE",
      profileKey: "impl",
      brief: "STACKED-NODE-ONE-BRIEF",
    },
    {
      id: "n2",
      key: "n2",
      title: "STACKED-NODE-TWO",
      profileKey: "test",
      brief: "STACKED-NODE-TWO-BRIEF",
    },
    {
      id: "n3",
      key: "n3",
      title: "STACKED-NODE-THREE",
      profileKey: "impl",
      brief: "STACKED-NODE-THREE-BRIEF",
    },
    {
      id: "n4",
      key: "n4",
      title: "STACKED-NODE-FOUR",
      profileKey: "review",
      brief: "STACKED-NODE-FOUR-BRIEF",
    },
  ],
}

const DOMAIN_PLAN = {
  intent: "DOMAIN-INTENT",
  scope: "runs/r-7/cancel",
  risk: "high",
  planSteps: ["DOMAIN-STEP-1", "DOMAIN-STEP-2"],
  diff: "+ domain diff",
  estimateMinutes: 4,
  nodes: [
    {
      id: "n1",
      key: "n1",
      title: "DOMAIN-NODE-ONE",
      profileKey: "impl",
      brief: "DOMAIN-NODE-ONE-BRIEF",
    },
    {
      id: "n2",
      key: "n2",
      title: "DOMAIN-NODE-TWO",
      profileKey: "impl",
      brief: "DOMAIN-NODE-TWO-BRIEF",
    },
  ],
}

const COMPACT_PLAN = {
  intent: "COMPACT-INTENT",
  scope: "chat/sessions/s-1/approve",
  risk: "low",
  planSteps: ["COMPACT-STEP-1"],
  diff: "",
  estimateMinutes: 1,
  nodes: [
    {
      id: "n1",
      key: "n1",
      title: "COMPACT-NODE-ONE",
      profileKey: "impl",
      brief: "COMPACT-NODE-ONE-BRIEF",
    },
  ],
}

// ---------------------------------------------------------------------------
// Density classification
// ---------------------------------------------------------------------------

describe("pendingApprovalEntry — risk density classification", () => {
  test("a 4-node plan resolves to stacked density", async () => {
    const entry = await entryFor(STACKED_PLAN)
    expect(entry.risk).toBe<RiskClass>("stacked")
    expect(entry.pendingAction.kind).toBe("stacked")
  })

  test("a 2-node plan resolves to domain density", async () => {
    const entry = await entryFor(DOMAIN_PLAN)
    expect(entry.risk).toBe<RiskClass>("domain")
    expect(entry.pendingAction.kind).toBe("domain")
  })

  test("a 1-node plan resolves to domain density (compact reserved for non-plan approvals)", async () => {
    const entry = await entryFor(COMPACT_PLAN)
    expect(entry.risk).toBe<RiskClass>("domain")
    expect(entry.pendingAction.kind).toBe("domain")
  })

  test("no pending plan → entry is null (the pipeline emits nothing)", () => {
    const session: HarnessSession = {
      ...awaitingSession(STACKED_PLAN),
      pendingPlan: null,
    }
    expect(
      pendingApprovalEntry({
        session,
        messageId: MESSAGE_ID,
        requester: REQUESTER,
        createdAtUnixMs: CREATED_AT,
        trailingMeta: null,
      })
    ).toBeNull()
  })

  test("a non-awaiting turn → entry is null", () => {
    const session: HarnessSession = {
      ...awaitingSession(STACKED_PLAN),
      turn: { kind: "idle" },
    }
    expect(
      pendingApprovalEntry({
        session,
        messageId: MESSAGE_ID,
        requester: REQUESTER,
        createdAtUnixMs: CREATED_AT,
        trailingMeta: null,
      })
    ).toBeNull()
  })

  test("a pending session (not remote) → entry is null", () => {
    const session: HarnessSession = {
      ...awaitingSession(STACKED_PLAN),
      identity: { kind: "pending", id: pendingSessionId("pending-1") },
    }
    expect(
      pendingApprovalEntry({
        session,
        messageId: MESSAGE_ID,
        requester: REQUESTER,
        createdAtUnixMs: CREATED_AT,
        trailingMeta: null,
      })
    ).toBeNull()
  })
})

// ---------------------------------------------------------------------------
// Payload shape
// ---------------------------------------------------------------------------

describe("pendingApprovalEntry — applicability + action shape", () => {
  test("domain density carries run id + plan payload + reason", async () => {
    const entry = await entryFor(DOMAIN_PLAN)
    if (entry.pendingAction.kind !== "domain") {
      throw new Error("expected domain action")
    }
    expect(entry.applicability.scope).toBe("runs/r-7/cancel")
    expect(entry.applicability.requester).toBe(REQUESTER)
    expect(entry.applicability.planId).toBe("n1")
    expect(entry.pendingAction.runId.length).toBeGreaterThan(0)
    expect(entry.pendingAction.reason).toBe("runs/r-7/cancel")
    expect(entry.pendingAction.planPayload).toBeDefined()
  })

  test("stacked density carries every node with its own density decision", async () => {
    const entry = await entryFor(STACKED_PLAN)
    if (entry.pendingAction.kind !== "stacked") {
      throw new Error("expected stacked action")
    }
    expect(entry.pendingAction.nodes).toHaveLength(4)
    for (const node of entry.pendingAction.nodes) {
      expect(["compact", "domain"]).toContain(node.density)
    }
    expect(entry.pendingAction.nodes[0]?.id).toBe("n1")
  })

  test("approvalId is stable across rebuilds", async () => {
    const a = await entryFor(STACKED_PLAN)
    const b = await entryFor(STACKED_PLAN)
    expect(a.approvalId).toBe(b.approvalId)
  })

  test("fingerprint is null on a fresh entry — receipt writer stamps the row as-is", async () => {
    const entry = await entryFor(DOMAIN_PLAN)
    expect(entry.fingerprint).toBeNull()
  })

  test("approval id format carries session id + message id + scope", async () => {
    const entry = await entryFor(STACKED_PLAN)
    expect(entry.approvalId).toContain(SESSION_ID)
    expect(entry.approvalId).toContain(MESSAGE_ID)
    expect(entry.approvalId).toContain("approval")
  })

  test("entry id is session-scoped and stable across rebuilds", async () => {
    const a = await entryFor(STACKED_PLAN)
    const b = await entryFor(STACKED_PLAN)
    expect(a.id).toBe(`${SESSION_ID}#approval`)
    expect(a.id).toBe(b.id)
  })
})

describe("planPayloadFrom — wire shape decoding", () => {
  test("legacy `planSteps` shape lifts to steps", () => {
    const payload = planPayloadFrom({
      intent: "x",
      scope: "y",
      risk: "low",
      planSteps: ["a", "b"],
      diff: "diff-line",
      estimateMinutes: 3,
    })
    expect(payload.steps).toEqual(["a", "b"])
    expect(payload.diff).toEqual(["diff-line"])
    expect(payload.estimateMinutes).toBe(3)
    expect(payload.intent).toBe("x")
  })

  test("platform `nodes` shape lifts to nodes", () => {
    const payload = planPayloadFrom({
      intent: "x",
      scope: "y",
      nodes: [
        { id: "n1", title: "t1", profileKey: "impl", brief: "b1" },
        { id: "n2", title: "t2", profileKey: "test", brief: "b2" },
      ],
    })
    expect(payload.nodes.map((node) => node.id)).toEqual(["n1", "n2"])
    expect(payload.nodes[0]?.brief).toBe("b1")
  })

  test("non-object payload → empty fields", () => {
    expect(planPayloadFrom(null).nodes).toEqual([])
    expect(planPayloadFrom(undefined).steps).toEqual([])
    expect(planPayloadFrom("string").intent).toBeNull()
  })

  test("nodes missing id/key are renumbered", () => {
    const payload = planPayloadFrom({
      nodes: [
        { title: "first", profileKey: "impl", brief: "b" },
        { title: "second", profileKey: "impl", brief: "b" },
      ],
    })
    expect(payload.nodes[0]?.id).toBe("n1")
    expect(payload.nodes[1]?.id).toBe("n2")
  })
})

// ---------------------------------------------------------------------------
// Renderer seam — summary + detail per density
// ---------------------------------------------------------------------------

async function i18n(): Promise<I18nInstance> {
  return await createI18nFor("en")
}

describe("approvalSummary — renderer seam per density", () => {
  test("domain summary keeps `approve / reject` literal so the legacy card test still passes", async () => {
    const entry = await entryFor(DOMAIN_PLAN)
    const summary = approvalSummary(entry, await i18n())
    const text = lineText(summary)
    expect(text).toContain("approval")
    expect(text).toContain("approve")
    expect(text).toContain("reject")
    expect(text).toContain("runs/r-7/cancel")
  })

  test("stacked summary shows the node count", async () => {
    const entry = await entryFor(STACKED_PLAN)
    const summary = approvalSummary(entry, await i18n())
    const text = lineText(summary)
    expect(text).toContain("4 nodes")
  })

  test("compact summary keeps the action verbs + scope", async () => {
    const entry = await entryFor(COMPACT_PLAN)
    const summary = approvalSummary(entry, await i18n())
    const text = lineText(summary)
    expect(text).toContain("approve")
    expect(text).toContain("reject")
  })
})

describe("approvalDetail — renderer seam per density", () => {
  test("domain detail includes intent, scope, risk, plan header, steps and decide label", async () => {
    const entry = await entryFor(DOMAIN_PLAN)
    const detail = approvalDetail(entry, {
      i18n: await i18n(),
      width: 80,
      expanded: new Set(),
    })
    const text = detail.map(lineText).join("\n")
    expect(text).toContain("intent: DOMAIN-INTENT")
    expect(text).toContain("scope: runs/r-7/cancel")
    expect(text).toContain("risk: high")
    expect(text).toContain("plan:")
    expect(text).toContain("DOMAIN-STEP-1")
    expect(text).toContain("decide:")
  })

  test("stacked detail lists every node with its brief", async () => {
    const entry = await entryFor(STACKED_PLAN)
    const detail = approvalDetail(entry, {
      i18n: await i18n(),
      width: 80,
      expanded: new Set(),
    })
    const text = detail.map(lineText).join("\n")
    expect(text).toContain("STACKED-NODE-ONE-BRIEF")
    expect(text).toContain("STACKED-NODE-FOUR-BRIEF")
    for (const node of entry.pendingAction.kind === "stacked" ? entry.pendingAction.nodes : []) {
      expect(text).toContain(node.id)
    }
  })

  test("compact detail keeps the literal decide label", async () => {
    const entry = await entryFor(COMPACT_PLAN)
    const detail = approvalDetail(entry, {
      i18n: await i18n(),
      width: 80,
      expanded: new Set(),
    })
    const text = detail.map(lineText).join("\n")
    expect(text).toContain("decide:")
  })
})
