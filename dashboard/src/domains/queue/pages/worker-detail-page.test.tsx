import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { RequirePermission } from "@/app/layout/require-permission"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

import { resetPool } from "@/domains/queue/api/pool.store"
import { QueuePage } from "@/domains/queue/pages/queue-page"

import { WorkerDetailPage } from "./worker-detail-page"

/* The screen serves the mock seeds, and whether it does is normally an
   environment variable that is not committed. Pinning it here makes this a
   test of the screen rather than of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* A load failure is the *third* reading of "there is no worker here", and the
   only one of the three that is an error — so it has to be reachable. The
   store is the query's only source, so failing it is failing the query, and
   the flag is flipped per test rather than per file because every other case
   below needs the same store working normally.
 *
 * The loading frame, by contrast, has no test here and cannot have one: the
 * mock resolves inside the same `act` flush that renders it, so there is no
 * frame in which the skeleton exists to be found. Recorded here rather than
 * papered over with an assertion that would pass on a page with no skeleton in
 * it at all. */
let poolFails = false

vi.mock("@/domains/queue/api/pool.store", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("@/domains/queue/api/pool.store")>()
  return {
    ...actual,
    listWorkers: () => {
      if (poolFails) {
        throw new Error("the pool is unreachable")
      }
      return actual.listWorkers()
    },
  }
})

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws `No layout data found for index 0` when every element it measures is
   zero — which, in jsdom, they all are. Both screens here live inside the
   shell, and the shell's rail is itself a pane group, so the stub is what lets
   either of them mount at all. It costs this file nothing it could have had:
   jsdom computes no layout, so nothing below was ever going to check one. The
   height chain is hand-traced in `worker-detail-page.module.css`. */
vi.mock("react-resizable-panels", () => ({
  Group: ({
    children,
    className,
  }: {
    children: ReactNode
    className?: string
  }) => (
    <div className={className} data-test="split-pane">
      {children}
    </div>
  ),
  Panel: ({
    children,
    className,
    id,
  }: {
    children: ReactNode
    className?: string
    id?: string
  }) => (
    <div className={className} data-panel={id}>
      {children}
    </div>
  ),
  Separator: ({ className }: { className?: string }) => (
    <div role="separator" className={className} data-test="split-separator" />
  ),
}))

/* jsdom lays nothing out, and both screens under test depend on that: the
   pool's table body is virtualized, and the shell's rail is a resizable panel.
   Without a measured port the rows never render and the panel has no layout to
   restore. */
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
    value: 480,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1400,
  })

  /* `SplitPane` persists the shell rail's divider position, and in jsdom the
     layout it saves was measured against nothing. Nothing is ever read back. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

/* Every act on this page writes to the pool store the query reads, and two of
   the cases below are *about* that write. Putting the shift back between tests
   is what keeps them independent. */
beforeEach(() => {
  resetPool()
  poolFails = false
})

/* The rail links to every product screen, so a memory router that does not
   know those paths cannot render the shell at all. */
const RAIL_PATHS = [
  "/",
  "/tasks",
  "/runs",
  "/approvals",
  "/cost",
  "/sources",
  "/knowledge",
  "/verify",
  "/settings",
  "/identity",
  "/projects",
  "/compute",
  "/models",
  "/observability",
  "/components",
  "/login",
]

interface QueueSearch {
  q?: string
  w?: string
}

function buildRouteTree() {
  const rootRoute = createRootRoute()
  const blank = () => null

  /* The list route, in the shape it now has on disk: `/queue` answered by an
     index route, with the worker's page as a sibling underneath it rather than
     a child of a layout. Both halves keep their own search parameter. */
  const queue = createRoute({
    getParentRoute: () => rootRoute,
    path: "/queue",
    validateSearch: (search: Record<string, unknown>): QueueSearch => {
      const parsed: QueueSearch = {}
      if (typeof search.q === "string" && search.q) {
        parsed.q = search.q
      }
      if (typeof search.w === "string" && search.w) {
        parsed.w = search.w
      }
      return parsed
    },
    component: function QueueRoute() {
      const { q, w } = queue.useSearch()
      return <QueuePage search={q} workerSearch={w} />
    },
  })

  const worker = createRoute({
    getParentRoute: () => rootRoute,
    path: "/queue/workers/$workerId",
    component: function WorkerRoute() {
      const { workerId } = worker.useParams()
      // Through the real gate, because two of the cases below are about a
      // session that may read the pool and may not act on part of it.
      return (
        <RequirePermission
          permission="queue.view"
          title="Worker"
          crumbs={[
            { label: "observe", to: "/runs" },
            { label: "queue", to: "/queue" },
            { label: "worker" },
          ]}
        >
          <WorkerDetailPage workerId={workerId} />
        </RequirePermission>
      )
    },
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    createRoute({
      getParentRoute: () => rootRoute,
      path: "/runs/$runId",
      component: blank,
    }),
    queue,
    worker,
  ])
}

function mount(
  entries: string[],
  roles: Role[] = ["platform-admin"],
  projectRoles: Record<string, Role[]> = {}
) {
  const router = createRouter({
    routeTree: buildRouteTree(),
    history: createMemoryHistory({ initialEntries: entries }),
  })

  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles} projectRoles={projectRoles}>
        <QueryClientProvider
          client={
            new QueryClient({
              defaultOptions: {
                queries: { retry: false },
                mutations: { retry: false },
              },
            })
          }
        >
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )

  return router
}

const find = (selector: string) => document.querySelector(selector)
const all = (selector: string) =>
  Array.from(document.querySelectorAll(selector))
const text = (selector: string) => find(selector)?.textContent ?? ""
const href = (selector: string) =>
  decodeURIComponent(find(selector)?.getAttribute("href") ?? "")

const here = (router: ReturnType<typeof mount>) =>
  `${router.state.location.pathname}${router.state.location.searchStr}`

/** The worker's page once the one query has answered. */
async function open(
  workerId: string,
  roles?: Role[],
  projectRoles?: Record<string, Role[]>
) {
  const router = mount([`/queue/workers/${workerId}`], roles, projectRoles)
  await waitFor(() => expect(find('[data-test="worker-live"]')).not.toBeNull())
  return router
}

/* ------------------------------------------------------------------ *
 * Getting here
 * ------------------------------------------------------------------ */

describe("a row in the pool opens the container it names", () => {
  it("makes the identifier the link, and leaves the row alone", async () => {
    const router = mount(["/queue"])
    await waitFor(() => expect(all('[data-test="data-table"]')).toHaveLength(2))

    const link = find('[data-test="worker-link"]') as HTMLAnchorElement
    expect(link.tagName).toBe("A")

    /* The row is *not* a click target, and that is the point: drain and force
       stop sit at the other end of the same row, and a row-wide destination
       would swallow both. */
    const row = link.closest('[data-test="data-table-row"]') as HTMLElement
    expect(row).not.toBeNull()
    expect(row.tagName).not.toBe("A")
    expect(row.getAttribute("href")).toBeNull()
    expect(row.getAttribute("role")).not.toBe("link")

    fireEvent.click(link)

    await waitFor(() =>
      expect(router.state.location.pathname).toBe("/queue/workers/wk_2f8a")
    )
    await waitFor(() =>
      expect(find('[data-test="worker-live"]')).not.toBeNull()
    )
  })

  it("keeps the row's own acts reachable rather than navigating past them", async () => {
    const router = mount(["/queue"])
    await waitFor(() => expect(all('[data-test="data-table"]')).toHaveLength(2))

    // The drain button on the first row: pressing it must stay on the board.
    fireEvent.click(all('[data-test="worker-drain"]')[0])

    await waitFor(() => expect(here(router)).toBe("/queue"))
  })
})

/* ------------------------------------------------------------------ *
 * What is only knowable about this container
 * ------------------------------------------------------------------ */

describe("the live readings are the clocks the model computes", () => {
  it("sets lease, heartbeat and uptime as the durations, not as prose", async () => {
    // The seed's failing container: six seconds of lease left, silent for 74,
    // up for 1512. Every one of those is `formatDuration`'s spelling.
    await open("wk_e34d")

    const live = text('[data-test="worker-live"]')
    expect(text('[data-test="lease-meter"]')).toBe("00:06")
    expect(live).toContain("01:14")
    expect(live).toContain("25:12")
  })

  it("says what a lost heartbeat means, in the model's own words", async () => {
    await open("wk_e34d")

    expect(find('[data-test="lease-meter"]')?.getAttribute("data-heat")).toBe(
      "lost"
    )
    expect(text('[data-test="worker-lost-heartbeat"]')).toBe(
      "no heartbeat for 01:14 — the lease lapses and the item is requeued"
    )
  })

  it("draws no meter for a container that holds no lease", async () => {
    await open("wk_a07e")

    expect(text('[data-test="lease-meter"]')).toBe("—")
  })
})

describe("the hand-offs are the ones the product already mints", () => {
  it("sends a digest to every container on that image", async () => {
    await open("wk_2f8a")

    expect(href('[data-test="worker-image-link"]')).toBe(
      "/queue?w=sha256:9c41ab"
    )
  })

  it("links the run and the item the container is holding", async () => {
    await open("wk_2f8a")

    // The item is named as prose and handed off as two addresses: the run it
    // belongs to, and the item in the queue it came out of.
    expect(text('[data-test="worker-work"]')).toContain("wi_0101")
    expect(href('[data-test="worker-work-item"]')).toBe("/queue?q=wi_0101")
    expect(href('[data-test="worker-work-run"]')).toMatch(
      /^\/runs\/[0-9a-f]{8}$/
    )
    expect(find('[data-test="age-meter"]')).not.toBeNull()
  })

  it("redraws no table — it links to the queue with a filter applied", async () => {
    await open("wk_2f8a")

    expect(find('[data-test="data-table"]')).toBeNull()
  })

  it("says idle where the pool's own column says idle", async () => {
    await open("wk_a07e")

    expect(text('[data-test="worker-work"]')).toContain("idle")
    expect(find('[data-test="worker-work-item"]')).toBeNull()
  })
})

/* ------------------------------------------------------------------ *
 * The container is gone — the decision this screen turns on
 * ------------------------------------------------------------------ */

describe("a worker torn down while the page is open", () => {
  it("says the container is gone rather than raising an error", async () => {
    await open("wk_2f8a")

    // Through the page's own act, which is the ordinary way a container
    // disappears out from under this screen.
    fireEvent.click(find('[data-test="worker-force-stop"]')!)
    fireEvent.click(find('[data-test="confirm-dialog-confirm"]')!)

    await waitFor(
      () => expect(find('[data-test="worker-torn-down"]')).not.toBeNull(),
      { timeout: 4000 }
    )

    // Not an error, and not dressed as one. A container being torn down is the
    // pool doing what it is configured to do.
    expect(find('[role="alert"]')).toBeNull()
    expect(find('[data-test="worker-live"]')).toBeNull()
    expect(text('[data-test="worker-torn-down"]')).toContain("wk_2f8a")
  })

  it("names what it was holding and where that work went", async () => {
    await open("wk_2f8a")

    fireEvent.click(find('[data-test="worker-force-stop"]')!)
    fireEvent.click(find('[data-test="confirm-dialog-confirm"]')!)

    await waitFor(
      () => expect(find('[data-test="worker-torn-down"]')).not.toBeNull(),
      { timeout: 4000 }
    )

    // The store requeues an orphaned item rather than failing it, so the work
    // is still findable — and the state points at it both ways.
    expect(href('[data-test="worker-torn-down-item"]')).toBe("/queue?q=wi_0101")
    expect(href('[data-test="worker-torn-down-run"]')).toMatch(
      /^\/runs\/[0-9a-f]{8}$/
    )
    expect(text('[data-test="worker-torn-down"]')).toContain("queue")
  })

  it("offers no acts on a container that is not there", async () => {
    await open("wk_2f8a")

    fireEvent.click(find('[data-test="worker-force-stop"]')!)
    fireEvent.click(find('[data-test="confirm-dialog-confirm"]')!)

    await waitFor(
      () => expect(find('[data-test="worker-torn-down"]')).not.toBeNull(),
      { timeout: 4000 }
    )

    expect(find('[data-test="worker-drain"]')).toBeNull()
    expect(find('[data-test="worker-force-stop"]')).toBeNull()
  })
})

describe("a worker this session never saw", () => {
  it("names the missing id instead of saying 404", async () => {
    mount(["/queue/workers/wk_neverwas"])

    await waitFor(() =>
      expect(find('[data-test="worker-not-found"]')).not.toBeNull()
    )

    // The id is the only fact there is, and seeing it spelled back is how a
    // person catches the truncated paste that brought them here.
    expect(text('[data-test="worker-not-found"]')).toContain("wk_neverwas")
    expect(find('[data-test="worker-torn-down"]')).toBeNull()
    expect(find('[role="alert"]')).toBeNull()
  })

  it("hands the pool back, narrowed to the id, so it can be checked", async () => {
    mount(["/queue/workers/wk_neverwas"])

    await waitFor(() =>
      expect(find('[data-test="worker-not-found"]')).not.toBeNull()
    )

    expect(href('[data-test="worker-not-found-pool"]')).toBe(
      "/queue?w=wk_neverwas"
    )
    expect(href('[data-test="worker-not-found-queue"]')).toBe("/queue")
  })
})

/* ------------------------------------------------------------------ *
 * The four states, and the acts
 * ------------------------------------------------------------------ */

describe("the states this screen has", () => {
  it("keeps a load failure as an error, and only a load failure", async () => {
    poolFails = true
    mount(["/queue/workers/wk_2f8a"])

    await waitFor(() =>
      expect(find('[data-test="worker-error"]')).not.toBeNull()
    )

    /* This one really is an error, so it is the one that takes the role and
       the retry. A missing container must never be mistaken for it — and it is
       not: neither of the two "gone" states rendered here. */
    expect(find('[data-test="worker-error"]')?.getAttribute("role")).toBe(
      "alert"
    )
    expect(text('[data-test="worker-error"]')).toContain(
      "the pool is unreachable"
    )
    expect(find('[data-test="worker-retry"]')).not.toBeNull()
    expect(find('[data-test="worker-not-found"]')).toBeNull()
    expect(find('[data-test="worker-torn-down"]')).toBeNull()
  })

  it("keeps the way back on every one of them", async () => {
    mount(["/queue/workers/wk_neverwas"])
    await waitFor(() =>
      expect(find('[data-test="worker-not-found"]')).not.toBeNull()
    )

    const crumbs = all('[data-test="crumb"]').map((node) =>
      node.getAttribute("href")
    )
    expect(crumbs).toContain("/runs")
    expect(crumbs).toContain("/queue")
  })

  it("closes the screen to a session that may not read the pool", async () => {
    mount(["/queue/workers/wk_2f8a"], ["viewer"])

    await waitFor(() =>
      expect(find('[data-test="forbidden-state"]')).not.toBeNull()
    )
    expect(find('[data-test="worker-live"]')).toBeNull()
  })
})

describe("an act this shift may not perform", () => {
  /* Member on one project and viewer on the worker's own. The screen opens —
     `queue.view` is answered by the membership — and the two acts are refused,
     which is exactly the arrangement that makes a per-page permission answer
     necessary rather than a per-session one. */
  const SPLIT: Record<string, Role[]> = {
    p_comuki: ["member"],
    p_plexor: ["viewer"],
  }

  it("stays visible, and says what is missing", async () => {
    await open("wk_e34d", [], SPLIT)

    for (const selector of [
      '[data-test="worker-drain"]',
      '[data-test="worker-force-stop"]',
    ]) {
      const button = find(selector) as HTMLElement
      expect(button).not.toBeNull()
      expect(button.getAttribute("aria-disabled")).toBe("true")
      expect(button.getAttribute("data-denied")).toMatch(/^needs /)
    }
  })

  it("fires nothing when it is pressed", async () => {
    await open("wk_e34d", [], SPLIT)

    fireEvent.click(find('[data-test="worker-force-stop"]')!)
    // No dialog, because the act never started.
    expect(find('[data-test="confirm-dialog"]')).toBeNull()

    fireEvent.click(find('[data-test="worker-drain"]')!)

    // And the container is still up, unchanged, half a second later.
    await new Promise((resolve) => setTimeout(resolve, 400))
    expect(find('[data-test="worker-live"]')).not.toBeNull()
    expect(find('[data-test="worker-torn-down"]')).toBeNull()
  })
})

describe("the act that loses something asks first", () => {
  it("opens a dialog for force stop and names what is at stake", async () => {
    await open("wk_2f8a")

    fireEvent.click(find('[data-test="worker-force-stop"]')!)

    const dialog = find('[data-test="confirm-dialog"]')
    expect(dialog).not.toBeNull()
    expect(dialog?.textContent).toContain("wk_2f8a")
    expect(dialog?.textContent).toContain("back to the queue")
    // The pair keeps its words: neither is "ok" and neither is "cancel".
    expect(text('[data-test="confirm-dialog-confirm"]')).toBe("Force stop")
    expect(text('[data-test="confirm-dialog-cancel"]')).toBe("Leave it running")
  })

  it("does not ask for drain, because drain loses nothing", async () => {
    await open("wk_2f8a")

    fireEvent.click(find('[data-test="worker-drain"]')!)

    expect(find('[data-test="confirm-dialog"]')).toBeNull()
    // Draining a busy container leaves it up and finishing what it holds.
    await waitFor(
      () =>
        expect(
          find('[data-test="worker-state-badge"]')?.getAttribute("data-state")
        ).toBe("draining"),
      { timeout: 4000 }
    )
  })
})
