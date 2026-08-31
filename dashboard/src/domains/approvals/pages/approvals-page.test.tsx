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
import { describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { ApprovalsPage } from "@/domains/approvals/pages/approvals-page"
import { APPROVALS_SEED } from "@/shared/api/mock"
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
   The height chain is hand-traced in `approvals-page.module.css`. */
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

function renderScreen(roles: Role[] = ["platform-admin"]) {
  const rootRoute = createRootRoute({ component: ApprovalsPage })
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

async function queueReady(roles: Role[] = ["platform-admin"]) {
  renderScreen(roles)
  await waitFor(() =>
    expect(all('[data-test="approval-card"]').length).toBeGreaterThan(0)
  )
}

describe("the approvals queue, end to end over the seeds", () => {
  it("draws every waiting decision rather than a blank strip", async () => {
    // The failure this project has actually shipped from this shape: every gate
    // green on a screen that draws nothing, because jsdom computes no layout
    // and a broken height chain is invisible to it.
    await queueReady()

    // The mock queue is module state that earlier decisions can drain, so the
    // assertion is "no more than the seed and at least one", not an exact count.
    const cards = all('[data-test="approval-card"]')
    expect(cards.length).toBeGreaterThan(0)
    expect(cards.length).toBeLessThanOrEqual(APPROVALS_SEED.length)
    expect(find('[data-test="approvals-empty"]')).toBeNull()
  })

  it("puts a risk and a kind on every card", async () => {
    await queueReady()

    for (const card of all('[data-test="approval-card"]')) {
      expect(card.querySelector('[data-test="approval-type-badge"]')).not.toBeNull()
      expect(card.querySelector('[data-test="approval-risk-badge"]')).not.toBeNull()
    }
  })

  it("keeps every decision on the screen for a role that cannot take it", async () => {
    // Somebody who may read the queue and not decide on it should meet the acts
    // and learn what they need, not a screen with a hole in it.
    await queueReady(["viewer"])

    const approve = all('[data-test="approval-approve"]')
    expect(approve.length).toBeGreaterThan(0)
    for (const control of approve) {
      expect(control.getAttribute("aria-disabled")).toBe("true")
      expect(control.hasAttribute("disabled")).toBe(false)
      expect(control.getAttribute("data-denied")).toMatch(/^needs /)
    }

    // Reading the plan is not a decision, so the disclosures stay open.
    for (const control of all('[data-test="approval-details"]')) {
      expect(control.hasAttribute("aria-disabled")).toBe(false)
    }
  })
})
