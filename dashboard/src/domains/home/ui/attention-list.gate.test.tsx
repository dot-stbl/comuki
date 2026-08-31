import { createContext, useContext, type ReactNode } from "react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import { groupAttention, readAttention } from "@/domains/home/model/attention"
import { AttentionList } from "@/domains/home/ui/attention-list"
import type { RunStatus, RunSummary } from "@/domains/runs/model/types"
import { TestSession } from "@/shared/session/test-session"

/* The rows carry real `<Link>`s, so they only render inside a router. A memory
   router with the product's own paths gives them somewhere to point without
   dragging the app's route tree into a test about two buttons. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/runs/$runId", "/tasks", "/approvals"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function run(
  id: string,
  status: RunStatus,
  projectId: string,
  title: string
): RunSummary {
  return {
    id,
    projectId,
    app: "billing-api",
    title,
    status,
    current: "w1",
    model: "worker",
    cost: 0.4,
    tokens: 8000,
    durationSec: 300,
    done: false,
    workItems: [
      {
        id: "w1",
        profile: "verifier",
        label: "дождаться аппрува на раскатку",
        status,
        dependsOn: [],
      },
    ],
  }
}

/**
 * One shift, two projects, and the same person in two different roles in them.
 * This is the ordinary case rather than a corner: a project here is an
 * attribute of the row, so every list in the product mixes them.
 */
const RUNS: RunSummary[] = [
  run("aa11", "waiting", "p_test", "release the retry budget"),
  run("bb22", "waiting", "p_other", "release the pricing cache"),
  run("cc33", "failed", "p_test", "migrate the theme API"),
]

function mount() {
  const onApprove = vi.fn()
  const onStop = vi.fn()
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  render(
    <TestSession
      roles={["viewer"]}
      projectRoles={{ p_test: ["approver"], p_other: ["viewer"] }}
    >
      <SlotContext
        value={
          <AttentionList
            groups={groupAttention(readAttention(RUNS).items)}
            hidden={0}
            approvingId={null}
            cancellingId={null}
            onApprove={onApprove}
            onStop={onStop}
          />
        }
      >
        <RouterProvider router={router} />
      </SlotContext>
    </TestSession>
  )

  // Found by accessible name — the handle `denied` leaves alone, since the
  // tooltip it swaps in is the thing under test.
  const button = (act: string, title: string) =>
    screen.getByRole("button", { name: `${act} ${title}` })

  return { onApprove, onStop, button }
}

describe("a decision is the row's, so the permission is the row's", () => {
  it("puts the approve through on the project this session approves on", async () => {
    const { button, onApprove } = mount()

    await screen.findByText("release the retry budget")
    const approve = button("Approve", "release the retry budget")

    expect(approve.hasAttribute("aria-disabled")).toBe(false)
    fireEvent.click(approve)
    expect(onApprove).toHaveBeenCalledWith(RUNS[0])
  })

  it("refuses the same act one row down, and names the project it refused on", async () => {
    const { button, onApprove } = mount()

    await screen.findByText("release the pricing cache")
    const approve = button("Approve", "release the pricing cache")

    // Same control, same place, same size — the row does not change shape per
    // viewer. What changes is that it explains itself and swallows the click.
    expect(approve.getAttribute("aria-disabled")).toBe("true")
    // The sentence rides `data-denied`. Inside a kit tooltip the button drops
    // the native title, because the tooltip is already carrying the same words
    // to the pointer and to the keyboard.
    expect(approve.getAttribute("data-denied")).toBe(
      "needs approver, project-admin or platform-admin on other"
    )
    // Naming the project is the point: "needs approver" alone would read as a
    // flat no to someone who approves all day on the row above.
    expect(approve.hasAttribute("disabled")).toBe(false)

    fireEvent.click(approve)
    expect(onApprove).not.toHaveBeenCalled()
  })

  it("gates stop on the same axis, one row at a time", async () => {
    const { button, onStop } = mount()

    await screen.findByText("release the retry budget")

    const live = button("Stop", "release the retry budget")
    expect(live.hasAttribute("aria-disabled")).toBe(false)
    fireEvent.click(live)
    expect(onStop).toHaveBeenCalledWith(RUNS[0])

    const refused = button("Stop", "release the pricing cache")
    expect(refused.getAttribute("aria-disabled")).toBe("true")
    expect(refused.getAttribute("data-denied")).toBe(
      "needs member, approver, project-admin, operator or platform-admin on other"
    )
    fireEvent.click(refused)
    expect(onStop).toHaveBeenCalledTimes(1)
  })

  it("gives a failed gate the one move it actually has", async () => {
    mount()

    await screen.findByText("migrate the theme API")

    // No approve and no stop on a failed run — the duty list offers neither on
    // that status either, and a second opinion here would be a drift.
    expect(
      screen.queryByRole("button", { name: "Approve migrate the theme API" })
    ).toBeNull()
    expect(
      screen.queryByRole("button", { name: "Stop migrate the theme API" })
    ).toBeNull()
    // Open is on every row, decided or not, so the column ends the same way.
    expect(
      screen.getByRole("link", { name: "Open migrate the theme API" })
    ).not.toBeNull()
  })

  it("buckets the rows worst-first and says why each bucket is here", async () => {
    mount()

    await screen.findByText("release the retry budget")

    const groups = [
      ...document.querySelectorAll("[data-test='attention-group']"),
    ].map((node) => node.getAttribute("data-status"))
    expect(groups).toEqual(["failed", "waiting"])

    expect(screen.getByText("stopped at a verification gate")).not.toBeNull()
    expect(screen.getByText("waiting on a human")).not.toBeNull()

    const rows = [
      ...document.querySelectorAll("[data-test='attention-row']"),
    ].map((node) => node.getAttribute("data-run"))
    expect(rows).toEqual(["cc33", "aa11", "bb22"])
  })
})
