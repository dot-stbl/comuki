import { createContext, useContext, type ReactNode } from "react"
import {
  QueryClient,
  QueryClientProvider,
} from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { TestSession } from "@/shared/session/test-session"

import { WorkTaskDetailPage } from "./work-task-detail-page"

/* The page reads through `useWorkTaskQuery`, which itself branches on
 * `env.useMock` to return a synthetic task in mock mode. Three cases —
 * present, missing, failed — and the third needs a stub, because the mock
 * branch never fails on its own. */

const mockQueryState = vi.hoisted(() => ({
  data: undefined as unknown,
  isLoading: false,
  isError: false,
  error: null as Error | null,
}))

vi.mock("@/domains/work/api/queries", () => ({
  useWorkTaskQuery: () => ({
    data: mockQueryState.data,
    isLoading: mockQueryState.isLoading,
    isError: mockQueryState.isError,
    error: mockQueryState.error,
    refetch: vi.fn(),
  }),
}))

/* `react-resizable-panels` solves for a layout in a `useLayoutEffect` and
 * throws when every element it measures is zero — which, in jsdom, they
 * all are. The same form-page / run-detail-page precedent uses a stub so
 * the screen mounts at all. The css height chain is hand-traced; jsdom
 * computes no layout. */
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

beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
})

function mount(workTaskId: string) {
  /* `FormPage` carries an `AppShell` whose rail reads the active route —
   * no router context means a `useLocation()` throws on the very first
   * render of the shell. The page itself only uses `Route.useParams` on
   * its host, which never runs in this test; the router here exists
   * purely to satisfy the shell. A blank child route stands in for the
   * detail route, and the actual page is slotted through a context so
   * `mount(<id>)` can hand the workTaskId straight in — the same shape
   * the run-detail-page test uses. */
  const SlotContext = createContext<ReactNode>(null)
  const Slot = () => <>{useContext(SlotContext)}</>

  const rootRoute = createRootRoute({ component: Slot })
  const blank = () => null
  const routeTree = rootRoute.addChildren(
    ["/", "/tasks", "/runs", "/sources"].map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    )
  )
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/tasks"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={["platform-admin"]}>
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          <SlotContext value={<WorkTaskDetailPage workTaskId={workTaskId} />}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const at = (test: string) => document.querySelector(`[data-test="${test}"]`)

function reset() {
  mockQueryState.data = undefined
  mockQueryState.isLoading = false
  mockQueryState.isError = false
  mockQueryState.error = null
}

beforeEach(() => {
  reset()
})

describe("what the screen answers before the work task is on it", () => {
  it("draws the task once it arrives", async () => {
    reset()
    mockQueryState.data = {
      id: "wt_abc",
      projectId: "00000000-0000-0000-0000-000000000000",
      title: "Wire the migration smoke test",
      status: "Active",
      resolutionOutcome: null,
      attemptOrdinal: 2,
      activeAttemptId: "att_xyz",
      visibility: "Project",
      missionId: null,
      sourceRefs: [
        {
          kind: "GitHub",
          externalId: "comuki/comuki#1234",
          displayName: "Migration smoke",
        },
      ],
      briefVersion: 4,
      createdAt: "2026-10-01T08:00:00.000Z",
      updatedAt: "2026-10-02T11:00:00.000Z",
    }

    mount("wt_abc")

    /* The facts list is the working surface; the loading frame is the
     * pre-data reading and the page never sits in it long enough to test it
     * directly. The mock query resolves in the same `act` flush that mounts
     * the page, so the existence of `data-test="work-facts"` after the
     * first paint is the same fact the run page proves with `run-graph`. */
    await waitFor(() => expect(at("work-facts")).not.toBeNull())

    expect(document.body.textContent).toContain("Wire the migration smoke test")
    /* The id rides as `task_<id>` in the breadcrumb because the title is
     * not yet loaded — the page's own header uses the title once it has
     * one, so the breadcrumb is the only place the id ever surfaces on
     * this screen when data is on it. */
    expect(document.body.textContent).toContain("wt_abc")
    /* The source-ref list renders one row per ref. */
    expect(at("work-source-ref")).not.toBeNull()
    expect(at("work-source-id")?.textContent).toBe("comuki/comuki#1234")
    expect(at("work-not-found")).toBeNull()
    expect(at("work-error")).toBeNull()
  })

  it("tells a stale link apart from a broken backend, and names the id", async () => {
    reset()
    /* `data === undefined` plus `!isLoading && !isError` lands the page on the
     * "not found" branch — the host has answered and this id was not in it. */
    mount("no_such_task")

    const missing = await waitFor(() => {
      const node = at("work-not-found")
      expect(node).not.toBeNull()
      return node as HTMLElement
    })

    expect(missing.getAttribute("data-state")).toBe("notFound")
    /* The id is the only part of the link the operator can take back to
     * whoever wrote it, so the state has to say it. */
    expect(missing.textContent).toContain("no_such_task")
    /* And a way out — a retry here would ask the same question again. */
    expect(at("work-not-found-back")?.getAttribute("href")).toBe("/tasks")
    expect(at("work-error")).toBeNull()
  })

  it("calls a failed read a failure, and offers the retry", async () => {
    reset()
    mockQueryState.isError = true
    mockQueryState.error = new Error("the swarm is unreachable")

    mount("wt_abc")

    /* The error branch's ScreenState lives inside the FormPage's column,
     * which is what waitFor is racing against — the form-page column mounts
     * on the same flush as the error state. Waiting on the retry button is
     * more reliable than waiting on the column root, because the button is
     * the last thing rendered and cannot exist without the column above it. */
    await waitFor(() => expect(at("work-retry")).not.toBeNull())

    /* The state lives at `data-test="work-error"`, but the assertion against
     * the retry button doubles as the proof the state is there. The
     * ScreenState contract gives every `error` state `role="alert"` — the
     * only one of the four kinds that interrupts a screen reader. */
    const state = at("work-error") as HTMLElement | null
    expect(state).not.toBeNull()
    expect(state?.getAttribute("role")).toBe("alert")
    expect(state?.getAttribute("data-state")).toBe("error")
    /* The retry button's aria-label is the common action, not the page's
     * own copy — shared actions read the same word everywhere. */
    const retry = at("work-retry")
    expect(retry?.getAttribute("aria-label")).toBeTruthy()
    expect(document.body.textContent).toContain("the swarm is unreachable")
    expect(at("work-not-found")).toBeNull()
  })

  it("says whose work task this is and which attempt is running", async () => {
    reset()
    mockQueryState.data = {
      id: "wt_abc",
      projectId: "00000000-0000-0000-0000-000000000000",
      title: "Patch the search resolver",
      status: "Blocked",
      resolutionOutcome: null,
      attemptOrdinal: 3,
      activeAttemptId: "att_qrs",
      visibility: "Mission",
      missionId: "msn_alpha",
      sourceRefs: [],
      briefVersion: 7,
      createdAt: "2026-09-30T08:00:00.000Z",
      updatedAt: "2026-10-02T11:00:00.000Z",
    }

    mount("wt_abc")

    await waitFor(() => expect(at("work-facts")).not.toBeNull())

    /* The status badge rides in the actions slot — a single chip that
     * says where the task stands. The summary line says the project
     * key and the status in the operator's own words. */
    const summary = at("work-summary") as HTMLElement
    expect(summary.textContent).toContain("Blocked")
    /* The active-attempt fact names the running attempt, not "none" —
     * `activeAttemptId` is non-null on this fixture by design. */
    expect(document.body.textContent).toContain("att_qrs")
    /* A task with no source tickets renders the empty notice rather than
     * an empty list — the only "what is absent" reading the page offers. */
    expect(at("work-no-source-refs")).not.toBeNull()
  })
})