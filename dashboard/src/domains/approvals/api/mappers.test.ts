import { describe, expect, it } from "vitest"

import { escalatedRunToApproval } from "@/domains/approvals/api/mappers"

/** Now, pinned — the waiting age is derived from instants. */
const NOW = Date.parse("2026-09-13T12:00:00Z")

const RUN = {
  id: "7e0b9d12-1111-2222-3333-444444444444",
  projectId: "b3d8a402-1111-2222-3333-444444444444",
  status: "escalated",
  createdAt: "2026-09-13T10:41:00Z",
  updatedAt: "2026-09-13T11:48:00Z",
} as const

describe("an escalated run onto the decision card", () => {
  it("carries the identity the decision needs: run, project, wait", () => {
    const approval = escalatedRunToApproval(RUN, NOW)

    expect(approval.id).toBe(RUN.id)
    expect(approval.runId).toBe(RUN.id)
    expect(approval.projectId).toBe(RUN.projectId)
    expect(approval.age).toBe("12 min")
  })

  it("says gate, and refuses to invent a risk or a reason", () => {
    const approval = escalatedRunToApproval(RUN, NOW)

    // The wire says *that* the run escalated, never why: the type is the
    // mechanism's own word, the risk is unread, and there are no planner
    // assumptions to disclose.
    expect(approval.type).toBe("gate")
    expect(approval.risk).toBeNull()
    expect(approval.assumptions).toEqual([])
    expect(approval.summary).toContain("escalated")
  })

  it("reads a long wait in hours, the way the seed's queue always has", () => {
    const longWait = escalatedRunToApproval(
      { ...RUN, updatedAt: "2026-09-13T08:20:00Z" },
      NOW
    )
    expect(longWait.age).toBe("3 h")
  })
})
