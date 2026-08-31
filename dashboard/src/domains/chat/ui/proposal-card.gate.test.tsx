import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { Proposal } from "@/domains/chat/model/types"
import { ProposalCard } from "@/domains/chat/ui/proposal-card"
import { TestSession } from "@/shared/session/test-session"

/**
 * The rule the console exists to keep, tested rather than rendered.
 *
 * Two cards, one act, two projects — which is the ordinary case here, not the
 * corner one: a conversation moves between projects the way a duty list does,
 * and the same person approves on one and only watches the next.
 */

/** `data-test`, the attribute this product marks with — not `data-testid`. */
const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

function proposal(id: string, projectId: string): Proposal {
  return {
    id,
    act: "plan.approve",
    summary: "approve the deploy gate on 5b1d7e40",
    projectId,
    subject: "5b1d7e40",
    steps: [{ profile: "verifier", label: "дождаться аппрува" }],
  }
}

function mount() {
  const onDecide = vi.fn()

  render(
    <TestSession
      roles={["member"]}
      projectRoles={{ p_test: ["approver"], p_other: ["viewer"] }}
    >
      <ProposalCard proposal={proposal("cp_mine", "p_test")} onDecide={onDecide} />
      <ProposalCard
        proposal={proposal("cp_theirs", "p_other")}
        onDecide={onDecide}
      />
    </TestSession>
  )

  const [mineConfirm, theirsConfirm] = screen.getAllByRole("button", {
    name: "Approve — approve the deploy gate on 5b1d7e40",
  })
  const [mineReject, theirsReject] = screen.getAllByRole("button", {
    name: "Reject — approve the deploy gate on 5b1d7e40",
  })

  return { onDecide, mineConfirm, mineReject, theirsConfirm, theirsReject }
}

describe("a proposal is never applied by anything but a press", () => {
  it("decides nothing on mount", () => {
    const { onDecide } = mount()
    // There is no effect on the card, no timer, and no default answer. The
    // assistant's job ended at the question.
    expect(onDecide).not.toHaveBeenCalled()
  })

  it("puts the decision through where the session may decide", () => {
    const { mineConfirm, mineReject, onDecide } = mount()

    expect(mineConfirm.hasAttribute("aria-disabled")).toBe(false)
    fireEvent.click(mineConfirm)
    expect(onDecide).toHaveBeenCalledWith("cp_mine", "confirmed")

    fireEvent.click(mineReject)
    expect(onDecide).toHaveBeenCalledWith("cp_mine", "rejected")
  })
})

describe("a refusal, one card down", () => {
  it("keeps the control, marks it and explains it", () => {
    const { theirsConfirm } = mount()

    expect(document.body.contains(theirsConfirm)).toBe(true)
    expect(theirsConfirm.getAttribute("aria-disabled")).toBe("true")
    expect(theirsConfirm.getAttribute("data-denied")).toBe(
      "needs approver, project-admin or platform-admin on other"
    )
    // Never `disabled`: it fires no pointer events, so the explanation would
    // be unreachable by pointer — and the control would leave the tab order.
    expect(theirsConfirm.hasAttribute("disabled")).toBe(false)
  })

  it("swallows the click", () => {
    const { theirsConfirm, theirsReject, onDecide } = mount()

    fireEvent.click(theirsConfirm)
    fireEvent.click(theirsReject)
    expect(onDecide).not.toHaveBeenCalled()
  })

  it("says the reason out loud, not only on hover", () => {
    mount()
    // A refusal discovered by hovering is a refusal most people never discover.
    expect(at("chat-proposal-denial")?.textContent).toBe(
      "needs approver, project-admin or platform-admin on other"
    )
  })

  it("refuses both halves, because both write a decision", () => {
    const { theirsReject } = mount()
    expect(theirsReject.getAttribute("aria-disabled")).toBe("true")
  })
})

describe("a decided proposal", () => {
  it("stops asking and keeps its place in the thread", () => {
    render(
      <TestSession roles={["member"]} projectRoles={{ p_test: ["approver"] }}>
        <ProposalCard
          proposal={{ ...proposal("cp_done", "p_test"), decision: "confirmed" }}
          onDecide={vi.fn()}
        />
      </TestSession>
    )

    expect(at("chat-proposal-confirm")).toBeNull()
    expect(at("chat-proposal-decided")).not.toBeNull()
  })
})

describe("the two halves keep their words", () => {
  it("names the act rather than drawing it", () => {
    // Action buttons in this product are icon buttons. These two are the
    // exception, and the reason is that they are one question: an answer given
    // with two pictures is an answer nobody is sure they gave.
    const { mineConfirm, mineReject } = mount()
    expect(mineConfirm.textContent).toBe("Approve")
    expect(mineReject.textContent).toBe("Reject")
  })

  it("uses the act's own words for a stop", () => {
    render(
      <TestSession roles={["member"]} projectRoles={{ p_test: ["member"] }}>
        <ProposalCard
          proposal={{
            ...proposal("cp_stop", "p_test"),
            act: "run.stop",
            summary: "stop 5b1d7e40",
          }}
          onDecide={vi.fn()}
        />
      </TestSession>
    )

    expect(at("chat-proposal-confirm")?.textContent).toBe("Stop")
    expect(at("chat-proposal-reject")?.textContent).toBe("Leave running")
  })
})
