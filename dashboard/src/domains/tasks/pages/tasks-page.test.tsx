import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, waitFor } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { toTask } from "@/domains/tasks/api/mappers"
import { matchesTaskQuery } from "@/domains/tasks/model/filter-tasks"
import { TasksPage } from "@/domains/tasks/pages/tasks-page"
import { TASKS_SEED } from "@/shared/api/mock"
import { TestSession } from "@/shared/session/test-session"
import type { Role } from "@/shared/session"

/* The screen serves the mock seeds, and whether it does is normally an
   environment variable that is not committed. Pinning it here makes this a test
   of the screen rather than of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws when every element it measures is zero — which, in jsdom, they all
   are. This screen has no pane group of its own; the shell's rail is one, and
   that is enough to throw. Stubbing it costs this test nothing it could have
   had: jsdom computes no layout, so nothing here was ever going to check one.
   The height chain is hand-traced in `tasks-page.module.css`. */
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
    value: 480,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1400,
  })
})

const RAIL_PATHS = [
  "/tasks",
  "/runs",
  "/queue",
  "/approvals",
  "/cost",
  "/sources",
  "/knowledge",
  "/verify",
  "/settings",
  "/projects",
  "/identity",
  "/compute",
  "/models",
  "/observability",
  "/components",
  "/login",
]

function renderScreen(focus?: string, roles: Role[] = ["platform-admin"]) {
  const rootRoute = createRootRoute({
    component: () => <TasksPage focus={focus} />,
  })
  const blank = () => null
  const routeTree = rootRoute.addChildren(
    RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    )
  )
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles}>
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          <RouterProvider router={router} />
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const find = (selector: string) => document.querySelector(selector)
const all = (selector: string) =>
  Array.from(document.querySelectorAll(selector))

async function backlogReady(focus?: string) {
  renderScreen(focus)
  await waitFor(() =>
    expect(find('[data-test="data-table"]')).not.toBeNull()
  )
}

describe("the backlog, end to end over the seeds", () => {
  it("draws rows rather than a blank strip", async () => {
    // The failure this project has actually shipped from this shape: every gate
    // green on a screen that draws nothing, because jsdom computes no layout
    // and a broken height chain is invisible to it. What is asserted is that
    // rows exist at all once the scroll port has a depth.
    await backlogReady()

    expect(all('[data-test="data-table-row"]').length).toBeGreaterThan(0)
    expect(find('[data-test="data-table-empty"]')).toBeNull()
    // The toolbar sits above the table and is part of the same chain.
    expect(find('[data-test="data-table-toolbar"]')).not.toBeNull()
  })

  it("arrives narrowed when the address bar brought a query", async () => {
    // `?q=` is what a global search hands over, and what a pasted link carries.
    // It seeds the toolbar's own field rather than filtering behind its back,
    // so the operator can see why the list is short.
    await backlogReady("web-app")

    const rows = all('[data-test="data-table-row"]')
    expect(rows.length).toBeGreaterThan(0)

    const search = find(
      '[data-test="data-table-search"]'
    ) as HTMLInputElement | null
    expect(search?.value).toBe("web-app")
  })

  it("counts what it is showing, not what it holds", async () => {
    // Against the seed rather than against the rendered rows: the body is
    // virtualized, so what is painted is a window onto the list and would make
    // this a test of the scroll port's depth instead of of the count.
    const matching = TASKS_SEED.map(toTask).filter((task) =>
      matchesTaskQuery(task, "web-app")
    ).length
    expect(matching).toBeGreaterThan(0)
    expect(matching).toBeLessThan(TASKS_SEED.length)

    await backlogReady("web-app")

    expect(find('[data-test="tasks-count"]')?.textContent).toBe(
      `${matching} shown`
    )
  })

  it("keeps the intake opener on the screen for a role that cannot use it", async () => {
    renderScreen(undefined, ["viewer"])
    await waitFor(() =>
      expect(find('[data-test="task-new"]')).not.toBeNull()
    )

    // Gated rather than hidden: a viewer who lands here should learn that
    // intake exists and what it takes to use it. `aria-disabled`, never
    // `disabled` — a disabled control fires no pointer events, so the sentence
    // on the tooltip would be unreachable.
    const opener = find('[data-test="task-new"]')
    expect(opener?.getAttribute("aria-disabled")).toBe("true")
    expect(opener?.hasAttribute("disabled")).toBe(false)
    expect(opener?.getAttribute("data-denied")).toBe(
      "needs member, approver, project-admin, operator or platform-admin"
    )
  })
})
