import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { Approval } from "@/domains/approvals/model/types"
import { ApprovalCard } from "@/domains/approvals/ui/approval-card"
import { TestSession } from "@/shared/session/test-session"

function approval(id: string, projectId: string): Approval {
  return {
    id,
    type: "deploy",
    app: "checkout-web",
    projectId,
    runId: "r_4417",
    age: "12 min",
    risk: "high",
    summary: "ship the retry budget change to staging",
    assumptions: ["the flake is in checkout, not in the gateway"],
  }
}

/**
 * One queue, two projects — which is the ordinary case, not the corner one.
 * The session approves on `p_test` and only watches `p_other`, so the two cards
 * must disagree with each other about the same act.
 */
function mount() {
  const onAction = vi.fn()
  // The card reads the run list to draw a plan preview; a client with no
  // queries in flight is enough, nothing here opens the details.
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  render(
    <TestSession
      roles={["member"]}
      projectRoles={{ p_test: ["approver"], p_other: ["viewer"] }}
    >
      <QueryClientProvider client={client}>
        <ApprovalCard approval={approval("ap_mine", "p_test")} onAction={onAction} />
        <ApprovalCard
          approval={approval("ap_theirs", "p_other")}
          onAction={onAction}
        />
      </QueryClientProvider>
    </TestSession>
  )

  // Queried by exact name and taken in document order — the card's own
  // button order is not this test's business. The name is the whole sentence
  // now that the label is a glyph: the words the tick stands in for, plus the
  // approval it decides, because both cards would otherwise be "approve".
  const [mineApprove, theirsApprove] = screen.getAllByRole("button", {
    name: "Approve the deploy for checkout-web",
  })
  const [mineReject] = screen.getAllByRole("button", {
    name: "Reject the deploy for checkout-web",
  })

  return { onAction, mineApprove, mineReject, theirsApprove }
}

describe("the approvals queue, decided per project", () => {
  it("puts the decision through on a project the session approves for", () => {
    const { mineApprove, mineReject, onAction } = mount()

    expect(mineApprove.hasAttribute("aria-disabled")).toBe(false)
    fireEvent.click(mineApprove)
    expect(onAction).toHaveBeenCalledWith("ap_mine", "approve")

    fireEvent.click(mineReject)
    expect(onAction).toHaveBeenCalledWith("ap_mine", "reject")
  })

  it("refuses the same act one card down, and names the project", () => {
    const { theirsApprove, onAction } = mount()

    // Still in the document, still where it was: hiding is for navigation, and
    // an action a role cannot use has to say what is missing instead.
    expect(document.body.contains(theirsApprove)).toBe(true)
    expect(theirsApprove.getAttribute("aria-disabled")).toBe("true")
    // The sentence lives on `data-denied`, which is where it always was. It is
    // no longer also on `title`: inside a kit tooltip the same words are
    // already on their way to the pointer, and `Button` drops the native one
    // rather than deliver them twice in two different shapes.
    expect(theirsApprove.getAttribute("data-denied")).toBe(
      "needs approver, project-admin or platform-admin on other"
    )
    // `disabled` would fire no pointer events, so the explanation would never
    // reach a pointer — and the control would leave the tab order as well.
    expect(theirsApprove.hasAttribute("disabled")).toBe(false)

    fireEvent.click(theirsApprove)
    expect(onAction).not.toHaveBeenCalled()
  })

  it("keeps reading the plan open to everyone", () => {
    mount()

    // A person who cannot approve is often exactly the one asked why.
    const details = screen.getAllByRole("button", { name: "Details" })
    for (const control of details) {
      expect(control.hasAttribute("aria-disabled")).toBe(false)
    }
  })
})
