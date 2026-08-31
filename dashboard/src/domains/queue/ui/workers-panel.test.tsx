import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { fireEvent, render, screen } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

import type { QueueItem, Worker, WorkerPool } from "@/domains/queue/model/types"

import { WorkerEmpty } from "./worker-empty"
import { WorkersPanel } from "./workers-panel"

/* The one column that needs a router renders a link to the run a busy worker
   is holding. Standing a whole route tree up to look at two buttons is worse
   than saying what a `Link` is here. */
vi.mock("@tanstack/react-router", () => ({
  Link: ({ children, ...rest }: { children: ReactNode }) => (
    <a href="#run" {...rest}>
      {children}
    </a>
  ),
}))

/* The virtualizer needs a scroll port with a depth and something watching it,
   and jsdom has neither — without these the body renders no rows and every
   assertion below passes by looking at an empty table. */
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
    value: 1400,
  })
})

const POOLS: WorkerPool[] = [
  { projectId: "p_test", minIdle: 0, maxIdle: 12 },
  { projectId: "p_other", minIdle: 0, maxIdle: 12 },
]

const HELD: QueueItem = {
  id: "wi_1",
  runId: "8f3c2a91",
  projectId: "p_test",
  profile: "implementer",
  label: "backfill the payout ledger",
  status: "running",
  ageSec: 412,
  claimedBy: "wk_live",
  blockedOn: [],
}

const QUEUED: QueueItem = {
  id: "wi_2",
  runId: "b3d8a402",
  projectId: "p_test",
  profile: "docs",
  label: "write up the retention window",
  status: "queued",
  ageSec: 664,
  claimedBy: null,
  blockedOn: [],
}

/** One worker per project, so a row's project is the only thing that differs. */
const WORKERS: Worker[] = [
  {
    id: "wk_live",
    projectId: "p_test",
    profile: "implementer",
    state: "busy",
    itemId: "wi_1",
    provider: "kubernetes",
    handle: "k8s/test/worker-implementer-live",
    heartbeatAgeSec: 3,
    leaseSec: 214,
    upSec: 1840,
    digest: "sha256:9c41ab",
  },
  {
    id: "wk_shut",
    projectId: "p_other",
    profile: "reviewer",
    state: "idle",
    itemId: null,
    provider: "docker",
    handle: "docker/other/worker-reviewer-shut",
    heartbeatAgeSec: 1,
    leaseSec: null,
    upSec: 240,
    digest: "sha256:9c41ab",
  },
]

function mount(
  props: Partial<React.ComponentProps<typeof WorkersPanel>> = {},
  projectRoles: Record<string, Role[]> = {
    p_test: ["project-admin"],
    p_other: ["viewer"],
  }
) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })

  return render(
    // No platform roles at all: every answer below has to come from the row's
    // own project, which is the arrangement under test.
    <TestSession roles={[]} projectRoles={projectRoles}>
      <QueryClientProvider client={client}>
        <WorkersPanel
          workers={WORKERS}
          items={[HELD, QUEUED]}
          pools={POOLS}
          projects={[
            { id: "p_test", key: "test", name: "Test project" },
            { id: "p_other", key: "other", name: "Other project" },
          ]}
          {...props}
        />
      </QueryClientProvider>
    </TestSession>
  )
}

const RUNS_STOP_ROLES =
  "needs member, approver, project-admin, operator or platform-admin"

describe("the pool's two acts, resolved per row", () => {
  it("opens both acts on the project this session administers", async () => {
    mount()

    const drain = screen.getByRole("button", { name: "Drain wk_live" })
    const stop = screen.getByRole("button", { name: "Force stop wk_live" })

    expect(drain.hasAttribute("aria-disabled")).toBe(false)
    expect(stop.hasAttribute("aria-disabled")).toBe(false)

    // The word the glyph stands in for moved from a native `title` to the kit
    // tooltip, which is reachable by focus as well as by pointer — the thing
    // `title` never was. Focus rather than hover: React Aria opens on focus
    // with no dwell, and what is under test is the wiring, not the timer.
    stop.focus()
    expect((await screen.findByRole("tooltip")).textContent).toBe("Force stop")
  })

  it("refuses the same acts one row down, and names the project it refused on", () => {
    // The reading this whole arrangement exists for: the same person, the same
    // screen, two rows, two answers. A flat "needs project-admin" would read as
    // a blanket no to someone who administers a pool all day.
    mount()

    const drain = screen.getByRole("button", { name: "Drain wk_shut" })
    const stop = screen.getByRole("button", { name: "Force stop wk_shut" })

    expect(drain.getAttribute("aria-disabled")).toBe("true")
    expect(stop.getAttribute("aria-disabled")).toBe("true")
    // Inside a kit tooltip the sentence arrives there rather than on a native
    // `title`, so it is not delivered twice in two shapes. `data-denied` is
    // where the reason itself lives, tooltip open or shut.
    expect(stop.getAttribute("data-denied")).toBe(`${RUNS_STOP_ROLES} on other`)
    expect(stop.getAttribute("title")).toBeNull()

    // `disabled` would drop them out of the tab order and kill the hover that
    // carries the sentence — the explanation would exist and be unreachable.
    expect(stop.hasAttribute("disabled")).toBe(false)
    expect(drain.hasAttribute("disabled")).toBe(false)
  })

  it("keeps a refused row the same shape as a live one", () => {
    mount()

    // Both rows offer both controls. Hiding them would make the table a
    // different shape per viewer and teach nobody what to ask for.
    expect(screen.getByRole("button", { name: "Drain wk_shut" })).toBeTruthy()
    expect(
      screen.getByRole("button", { name: "Force stop wk_shut" })
    ).toBeTruthy()
  })
})

describe("force stop asks first", () => {
  it("opens the dialog instead of firing, and says what is lost", () => {
    mount()

    expect(document.querySelector('[data-test="confirm-dialog"]')).toBeNull()

    fireEvent.click(screen.getByRole("button", { name: "Force stop wk_live" }))

    const dialog = document.querySelector('[data-test="confirm-dialog"]')
    expect(dialog).not.toBeNull()
    // The consequence, not a generic warning: the item this container is
    // holding goes back to the queue, and the operator is told which.
    expect(dialog?.textContent).toContain("wk_live")
    expect(dialog?.textContent).toContain("backfill the payout ledger")
    expect(dialog?.textContent).toContain("back to the queue")
  })

  it("closes on the cancel without touching the pool", () => {
    mount()

    fireEvent.click(screen.getByRole("button", { name: "Force stop wk_live" }))
    fireEvent.click(
      document.querySelector('[data-test="confirm-dialog-cancel"]')!
    )

    expect(document.querySelector('[data-test="confirm-dialog"]')).toBeNull()
    // The row is still there, still busy — nothing was decided.
    expect(screen.getByRole("button", { name: "Force stop wk_live" })).toBeTruthy()
  })

  it("closes once the act is confirmed", () => {
    mount()

    fireEvent.click(screen.getByRole("button", { name: "Force stop wk_live" }))
    fireEvent.click(
      document.querySelector('[data-test="confirm-dialog-confirm"]')!
    )

    expect(document.querySelector('[data-test="confirm-dialog"]')).toBeNull()
  })

  it("does not open at all on a row this session may not stop", () => {
    mount()

    fireEvent.click(screen.getByRole("button", { name: "Force stop wk_shut" }))

    expect(document.querySelector('[data-test="confirm-dialog"]')).toBeNull()
  })

  it("leaves drain unasked, because drain loses nothing", () => {
    // The two acts are not two intensities of one thing. Drain lets the item
    // in hand finish; putting a dialog on it would train the operator to click
    // through the one that matters.
    mount()

    fireEvent.click(screen.getByRole("button", { name: "Drain wk_live" }))

    expect(document.querySelector('[data-test="confirm-dialog"]')).toBeNull()
  })
})

describe("an empty pool says which kind of empty it is", () => {
  function emptyPool(items: QueueItem[]) {
    mount({ workers: [], items })
    return document.querySelector('[data-test="worker-empty"]')
  }

  it("reads a backlog as scale about to act", () => {
    const state = emptyPool([QUEUED])

    expect(state?.getAttribute("data-kind")).toBe("backlog")
    expect(state?.textContent).toContain("min idle = 0")
    expect(state?.textContent).toContain("queued and unclaimed")
  })

  it("reads no backlog as the configured resting state", () => {
    const state = emptyPool([])

    expect(state?.getAttribute("data-kind")).toBe("at-rest")
    expect(state?.textContent).toContain("min idle = 0")
    expect(state?.textContent).toContain("resting state")
  })

  it("tells the two apart in words, not only in a flag", () => {
    // Both pools are empty and both are correct. If the sentence were the same
    // the screen would be teaching the operator to ignore it.
    const { unmount } = mount({ workers: [], items: [QUEUED] })
    const withBacklog =
      document.querySelector('[data-test="worker-empty"]')?.textContent ?? ""
    unmount()

    mount({ workers: [], items: [] })
    const atRest =
      document.querySelector('[data-test="worker-empty"]')?.textContent ?? ""

    expect(withBacklog).not.toBe(atRest)
    expect(withBacklog.length).toBeGreaterThan(0)
    expect(atRest.length).toBeGreaterThan(0)
  })

  it("hands the way out of a filtered pool as an icon button", () => {
    // The one empty state with an act in it. It is an icon button like every
    // other act in this product — the sentence above it is what explains the
    // state, and the control only has to say what pressing it does. It says
    // that through its accessible name and, on hover and focus, its tooltip.
    const onClearFilters = vi.fn()
    render(
      <WorkerEmpty
        kind="filtered"
        backlog={0}
        minIdle={0}
        poolSize={4}
        onClearFilters={onClearFilters}
      />
    )

    const clear = screen.getByRole("button", { name: "Clear filters" })
    expect(clear.getAttribute("data-size")).toBe("icon-sm")
    expect(clear.textContent).toBe("")

    fireEvent.click(clear)
    expect(onClearFilters).toHaveBeenCalledTimes(1)
  })

  it("blames the pool being below target before it reassures anyone", () => {
    const state = (() => {
      mount(
        { workers: [], items: [QUEUED], pools: [
          { projectId: "p_test", minIdle: 2, maxIdle: 12 },
        ] }
      )
      return document.querySelector('[data-test="worker-empty"]')
    })()

    expect(state?.getAttribute("data-kind")).toBe("under-target")
    expect(state?.textContent).toContain("not a resting state")
  })
})
