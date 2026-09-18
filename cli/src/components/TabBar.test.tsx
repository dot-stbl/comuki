/**
 * Pure tests for the tab strip's approval badge derivation. The Ink
 * render itself is covered by the layout smoke tests in Chrome /
 * LayoutShell; the badge is the only logic here.
 */
import { describe, expect, it } from "bun:test"
import { approvalBadge } from "./TabBar"
import { newPendingSession } from "../lib/sessions"

describe("approvalBadge", () => {
  it("is null for a session with nothing to decide", () => {
    expect(approvalBadge(newPendingSession())).toBeNull()
    expect(approvalBadge({ awaitingApproval: false })).toBeNull()
  })

  it("is the exclamation mark while a plan awaits approval", () => {
    expect(approvalBadge({ awaitingApproval: true })).toBe("!")
    expect(
      approvalBadge({ ...newPendingSession(), awaitingApproval: true })
    ).toBe("!")
  })
})
