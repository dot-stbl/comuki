import { useMemo } from "react"
import { fireEvent, render, screen } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import type { RunSummary } from "@/domains/runs/model/types"
import { createRunColumns, getRunId } from "@/domains/runs/ui/runs-columns"
import { useSession, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { DataTable } from "@/shared/ui"

/* The virtualizer needs a scroll port with a depth and something watching it,
   and jsdom has neither — without these the body renders no rows at all and
   every assertion below would pass by looking at an empty table. Same stubs as
   `data-table.test.tsx`, for the same reason. */
beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
  Object.defineProperty(HTMLElement.prototype, "offsetHeight", {
    configurable: true,
    value: 320,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 960,
  })
})

function waitingRun(id: string, projectId: string, title: string): RunSummary {
  return {
    id,
    projectId,
    app: "checkout-web",
    title,
    status: "waiting",
    current: "w1",
    model: "worker",
    cost: 0.42,
    tokens: 8100,
    durationSec: 96,
    done: false,
    workItems: [
      {
        id: "w1",
        profile: "planner",
        label: "decide the retry budget",
        status: "waiting",
        dependsOn: [],
      },
    ],
  }
}

/**
 * Two runs standing on a human gate — the only state that offers the two acts
 * — in two different projects. This is the ordinary case, not an edge one: the
 * duty engineer watches the whole swarm, so every list mixes projects.
 */
const RUNS: RunSummary[] = [
  waitingRun("r_4417", "p_test", "flaky checkout retry"),
  waitingRun("r_5108", "p_other", "stale price cache"),
]

/**
 * The duty list exactly as `RunsPage` assembles it: the session is read by a
 * component and travels into the column factory as a value. That is the
 * arrangement under test as much as the buttons are — a `cell` runs as a plain
 * function while TanStack builds a row, so a `useCan` moved down into one would
 * throw at the first render, and a `useCan` left up here would answer for the
 * whole screen when the question belongs to the row.
 */
function DutyList({
  onApprove,
  onCancel,
}: {
  onApprove: (run: RunSummary) => void
  onCancel: (run: RunSummary) => void
}) {
  const session = useSession()

  const columns = useMemo(
    () =>
      createRunColumns({
        apps: [],
        projects: session.projects,
        profiles: [],
        approvingId: null,
        cancellingId: null,
        onApprove,
        onCancel,
        session,
      }),
    [onApprove, onCancel, session]
  )

  return (
    <DataTable
      columns={columns}
      data={RUNS}
      getRowId={getRunId}
      density="compact"
      /* The run column renders a router `Link` and there is no router here.
         Hiding it keeps this test on the actions column instead of dragging a
         whole route tree in to look at four buttons. */
      columnVisibility={{ id: false }}
    />
  )
}

/** Buttons found by accessible name — the one handle `denied` leaves alone. */
function controls(run: RunSummary) {
  return {
    approve: screen.getByRole("button", { name: `Approve ${run.title}` }),
    cancel: screen.getByRole("button", { name: `Cancel ${run.title}` }),
  }
}

function mount(roles: Role[], projectRoles: Record<string, Role[]> = {}) {
  const onApprove = vi.fn()
  const onCancel = vi.fn()

  render(
    <TestSession roles={roles} projectRoles={projectRoles}>
      <DutyList onApprove={onApprove} onCancel={onCancel} />
    </TestSession>
  )

  return {
    onApprove,
    onCancel,
    // `p_test` and `p_other` are the two projects `TestSession` hands out.
    here: controls(RUNS[0]),
    there: controls(RUNS[1]),
  }
}

describe("one list, two projects, two different answers", () => {
  it("puts a live Approve directly above an explained one", () => {
    // The whole point of the screen: approver here, viewer there, and both
    // rows on the board at once. A single answer for the session would be
    // wrong on one of these two rows whichever way it went.
    const { here, there } = mount(["viewer"], { p_test: ["approver"] })

    expect(here.approve.hasAttribute("aria-disabled")).toBe(false)

    expect(there.approve.getAttribute("aria-disabled")).toBe("true")
    // Naming the project is what stops the second row reading as a flat no to
    // someone who has been approving on the first one all shift. The sentence
    // rides the kit tooltip now, so the button drops its native title rather
    // than delivering it twice — `data-denied` is where the reason lives.
    expect(there.approve.getAttribute("data-denied")).toBe(
      "needs approver, project-admin or platform-admin on other"
    )
    expect(there.approve.getAttribute("title")).toBeNull()
    expect(there.approve.hasAttribute("disabled")).toBe(false)
  })

  it("decides on the row that was clicked and on no other", () => {
    const { here, there, onApprove } = mount(["viewer"], {
      p_test: ["approver"],
    })

    fireEvent.click(there.approve)
    expect(onApprove).not.toHaveBeenCalled()

    fireEvent.click(here.approve)
    expect(onApprove).toHaveBeenCalledTimes(1)
    expect(onApprove).toHaveBeenCalledWith(RUNS[0])
  })

  it("gates stopping a run per row too, not just approving one", () => {
    // `approver` carries `member`, so the project grant opens the cancel here
    // while the platform `viewer` leaves it shut on the row beneath.
    const { here, there, onCancel } = mount(["viewer"], {
      p_test: ["approver"],
    })

    expect(here.cancel.hasAttribute("aria-disabled")).toBe(false)
    expect(there.cancel.getAttribute("data-denied")).toBe(
      "needs member, approver, project-admin, operator or platform-admin on other"
    )

    fireEvent.click(there.cancel)
    expect(onCancel).not.toHaveBeenCalled()

    fireEvent.click(here.cancel)
    expect(onCancel).toHaveBeenCalledWith(RUNS[0])
  })

  it("shows the project each row belongs to, by its key", () => {
    mount(["member"])

    // Not the id and not the display name: the key is what the operator calls
    // it, and it is the word the denial sentence uses two cells to the right.
    expect(screen.getByText("test")).toBeTruthy()
    expect(screen.getByText("other")).toBeTruthy()
  })
})

describe("the duty list's two decisions, by role", () => {
  it("keeps both controls on a viewer's row, and says what each one needs", () => {
    const { here } = mount(["viewer"])

    // Still in the document, in the same cell, at the same size. Hiding them
    // would leave a viewer with a row that looks finished and no idea that a
    // decision is owed on it, or by whom.
    expect(document.body.contains(here.approve)).toBe(true)
    expect(document.body.contains(here.cancel)).toBe(true)

    expect(here.approve.getAttribute("aria-disabled")).toBe("true")
    expect(here.approve.getAttribute("data-denied")).toBe(
      "needs approver, project-admin or platform-admin on test"
    )

    expect(here.cancel.getAttribute("aria-disabled")).toBe("true")
    expect(here.cancel.getAttribute("data-denied")).toBe(
      "needs member, approver, project-admin, operator or platform-admin on test"
    )

    // `disabled` would drop the pair out of the tab order and kill the hover
    // that carries the sentence — the explanation would exist and be
    // unreachable. `denied` is the whole difference.
    expect(here.approve.hasAttribute("disabled")).toBe(false)
    expect(here.cancel.hasAttribute("disabled")).toBe(false)
  })

  it("lets a viewer click, and nothing happens", () => {
    const { here, onApprove, onCancel } = mount(["viewer"])

    fireEvent.click(here.approve)
    fireEvent.click(here.cancel)

    expect(onApprove).not.toHaveBeenCalled()
    expect(onCancel).not.toHaveBeenCalled()
  })

  it("opens the run's cancel to a member and leaves the plan shut", () => {
    const { here, onApprove, onCancel } = mount(["member"])

    // The matrix is not a ladder, and this row is where it shows: stopping a
    // run is a member's, approving a plan is not.
    expect(here.cancel.hasAttribute("aria-disabled")).toBe(false)
    fireEvent.click(here.cancel)
    expect(onCancel).toHaveBeenCalledWith(RUNS[0])

    expect(here.approve.getAttribute("aria-disabled")).toBe("true")
    fireEvent.click(here.approve)
    expect(onApprove).not.toHaveBeenCalled()
  })

  it("puts the same button through for an approver", async () => {
    const { here, onApprove } = mount(["approver"])

    expect(here.approve.hasAttribute("aria-disabled")).toBe(false)
    // The tooltip is the act's own name again, not an apology for it. Focus
    // rather than hover: React Aria opens on focus with no dwell, and the
    // wiring is what is under test, not the warmup timer.
    here.approve.focus()
    expect((await screen.findByRole("tooltip")).textContent).toBe("Approve")

    fireEvent.click(here.approve)

    expect(onApprove).toHaveBeenCalledWith(RUNS[0])
  })

  it("shows an operator the same row as an approver, minus the approval", () => {
    // Operator is platform ops. It stops runs all day and still cannot approve
    // a plan, which is the one grant the extension order would have implied.
    const { here } = mount(["operator"])

    expect(here.cancel.hasAttribute("aria-disabled")).toBe(false)
    expect(here.approve.getAttribute("aria-disabled")).toBe("true")
  })

  it("carries a platform role into every project at once", () => {
    // The other half of the rule: a platform grant is not scoped, so it opens
    // both rows. A row-level answer is not the same as a per-project ceiling.
    const { here, there } = mount(["platform-admin"])

    expect(here.approve.hasAttribute("aria-disabled")).toBe(false)
    expect(there.approve.hasAttribute("aria-disabled")).toBe(false)
  })
})
