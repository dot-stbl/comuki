import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { Approval } from "@/domains/approvals/model/types"
import { ApprovalCard } from "@/domains/approvals/ui/approval-card"
import { TestSession } from "@/shared/session/test-session"

/**
 * What the surface itself says, as opposed to who is allowed to press it —
 * that is `approval-card.gate.test.tsx` and it stayed where it was.
 *
 * Queried on `data-test`, which is the attribute this product writes; there is
 * no `data-testid` anywhere in the tree for `getByTestId` to find.
 */
function approval(overrides: Partial<Approval> = {}): Approval {
  return {
    id: "ap_1",
    type: "deploy",
    app: "checkout-web",
    projectId: "p_test",
    runId: "r_4417",
    age: "12 min",
    risk: "high",
    summary: "ship the retry budget change to staging",
    assumptions: [
      "the flake is in checkout, not in the gateway",
      "staging mirrors production's retry budget",
    ],
    ...overrides,
  }
}

function mount(item: Approval, busy = false) {
  const onAction = vi.fn()
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  const { container } = render(
    <TestSession roles={["approver"]}>
      <QueryClientProvider client={client}>
        <ApprovalCard approval={item} busy={busy} onAction={onAction} />
      </QueryClientProvider>
    </TestSession>
  )

  const mark = (name: string) =>
    container.querySelector(`[data-test="${name}"]`)

  return { onAction, container, mark }
}

describe("the approval surface", () => {
  it("names the kind of decision with the word the model stores", () => {
    const { mark } = mount(approval({ type: "deploy" }))

    // Not `Deploy`. The chip and the button's own sentence ("approve the deploy
    // for checkout-web") have to be one vocabulary, not two spellings of one.
    const chip = mark("approval-type-badge")
    expect(chip?.textContent).toBe("deploy")
    expect(chip?.getAttribute("data-type")).toBe("deploy")
  })

  it("says the risk in a word as well as a hue", () => {
    const { mark } = mount(approval({ risk: "high" }))

    const badge = mark("approval-risk-badge")
    expect(badge?.textContent).toBe("high")
    expect(badge?.getAttribute("data-risk")).toBe("high")
    // The mark beside the word is the second channel: hue alone says nothing
    // in greyscale, and nothing at all to a red-green eye.
    expect(badge?.querySelector("svg")).not.toBeNull()
  })

  it("keeps the detail behind a disclosure that says which way it points", () => {
    const { mark } = mount(approval())

    const details = screen.getByRole("button", { name: "Details" })
    expect(details.getAttribute("aria-expanded")).toBe("false")
    expect(mark("approval-detail")).toBeNull()

    fireEvent.click(details)

    const open = screen.getByRole("button", { name: "Hide" })
    expect(open.getAttribute("aria-expanded")).toBe("true")
    expect(mark("approval-detail")).not.toBeNull()
  })

  it("lists every planner assumption once it is open", () => {
    const item = approval()
    mount(item)

    fireEvent.click(screen.getByRole("button", { name: "Details" }))

    for (const line of item.assumptions) {
      expect(screen.getByText(line)).toBeTruthy()
    }
  })

  it("offers the two panes a baseline decision compares", () => {
    const { container } = mount(approval({ type: "baseline" }))

    fireEvent.click(screen.getByRole("button", { name: "Details" }))

    // Scoped to the panes: the type chip on the header line says `baseline`
    // too, and that is the point — the chip and the left-hand pane are the
    // same word about two different things.
    const captions = [...container.querySelectorAll("figcaption")].map(
      (node) => node.textContent
    )
    expect(captions).toEqual(["baseline", "new"])
  })

  it("disables the three decisions while one is in flight", () => {
    mount(approval(), true)

    // `disabled`, and that is right here: busy is not a denial. The refusal
    // case is `aria-disabled` and lives in the gate test.
    const approve = screen.getByRole("button", {
      name: "Approve the deploy for checkout-web",
    })
    expect(approve.hasAttribute("disabled")).toBe(true)
    expect(approve.hasAttribute("data-denied")).toBe(false)
  })
})
