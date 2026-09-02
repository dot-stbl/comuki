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
import { resetSeedModels } from "@/shared/api/mock/models.store"
import { TestSession } from "@/shared/session/test-session"
import type { Role } from "@/shared/session"

import { ModelsPage } from "./models-page"

/* The screen serves the mock seeds, and whether it does is normally an
   environment variable that is not committed. Pinning it here makes this a test
   of the screen rather than of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws when everything it measures is zero, which in jsdom is everything.
   This screen has no pane group of its own — the shell's rail is one, and that
   is enough to throw. The height chain is hand-traced in
   `models-page.module.css`; jsdom could never have checked it anyway. */
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
   and jsdom has neither — without these the body renders no rows and every
   assertion below would pass by looking at an empty table. */
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

beforeEach(() => {
  resetSeedModels()
})

const rootRoute = createRootRoute({ component: ModelsPage })
const blank = () => null
const routeTree = rootRoute.addChildren(
  [
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
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function renderScreen(roles: Role[] = ["platform-admin"]) {
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

/** The screen once its one query has answered — all three tables. */
async function screenReady(roles: Role[] = ["platform-admin"]) {
  renderScreen(roles)
  await waitFor(() => expect(all('[data-test="data-table"]')).toHaveLength(3))
}

describe("the model registry, end to end over the seeds", () => {
  it("draws all four sections rather than a blank strip", async () => {
    // The failure this project has actually shipped from this shape: every gate
    // green on a screen that draws nothing. jsdom sees no layout, so what is
    // asserted is that rows exist at all once the port has a depth.
    await screenReady()

    expect(find('[data-test="models-proxy"]')).not.toBeNull()
    expect(find('[data-test="models-endpoints"]')).not.toBeNull()
    expect(find('[data-test="models-keys"]')).not.toBeNull()
    expect(find('[data-test="models-routing"]')).not.toBeNull()
    expect(all('[data-test="data-table-row"]').length).toBeGreaterThan(0)
    expect(find('[data-test="data-table-empty"]')).toBeNull()
    expect(all('[data-test="budget-meter"]').length).toBeGreaterThan(0)
  })

  it("says the proxy is off and what that stops, not only that it is off", async () => {
    await screenReady()

    const panel = find('[data-test="proxy-panel"]')!
    expect(panel.getAttribute("data-enabled")).toBeNull()
    expect(find('[data-test="proxy-state"]')?.textContent).toContain("off")
    expect(panel.textContent).toContain("Spend keys are not checked")
    expect(panel.textContent).toContain("budgets are not enforced")
    expect(panel.textContent).toContain("nothing is metered")
  })

  it("keeps the cost per run, marked as the last window rather than as now", async () => {
    // Removing the figure would hide the argument for turning the proxy back
    // on; showing it as current would be a lie. It stays, and it says so.
    await screenReady()

    const figures = find('[data-test="proxy-figures"]')!
    expect(figures.textContent).toContain("cost per run")
    expect(figures.textContent).toContain("$0.41")
    expect(find('[data-test="proxy-window"]')?.textContent).toContain(
      "last metered over"
    )
    expect(find('[data-test="proxy-window"]')?.textContent).toContain(
      "not current"
    )
  })

  it("draws the burn sparkline beside the figures, with its peak in words", async () => {
    await screenReady()

    const burn = find('[data-test="proxy-burn"]')
    expect(burn).not.toBeNull()

    // A line with a vertex per metered hour, and the words that say the
    // reading it confirms — the line is never the only carrier.
    const line = find('[data-test="proxy-burn"] [data-test="sparkline-line"]')
    expect(line?.getAttribute("points")?.split(" ")).toHaveLength(24)
    expect(burn?.textContent).toContain("peak $3.42 at 16:00")
    expect(
      find('[data-test="proxy-burn"] [data-test="sparkline"]')?.getAttribute("aria-label")
    ).toContain("peak $3.42 at 16:00")
  })

  it("marks a budget that is recorded but not being applied", async () => {
    await screenReady()

    // Every meter on the screen: with the proxy off, none of these caps is a
    // limit, and a solid bar would claim otherwise.
    const meters = all('[data-test="budget-meter"]')
    expect(meters.length).toBeGreaterThan(0)
    expect(
      meters.every((meter) => meter.getAttribute("data-enforced") === null)
    ).toBe(true)
  })

  it("opens on the key that already stopped working", async () => {
    // Worst first, and "worst" is not fullest: a run failing on an expired key
    // is happening now, while a cap is a decision somebody still has time to
    // make.
    await screenReady()

    const expiry = all('[data-test="key-expiry"]')
    expect(expiry[0].getAttribute("data-lapsed")).toBe("")
    expect(expiry[0].textContent).toBe("3 days ago")
  })

  it("shows the key at ninety percent of its cap as one to decide about", async () => {
    await screenReady()

    const near = all('[data-test="budget-meter"][data-heat="near"]')
    expect(near).toHaveLength(1)
    // A meter states its own numbers: the figures are the reading and the bar
    // is drawn on top of them.
    expect(near[0].textContent).toContain("$361.40")
    expect(near[0].textContent).toContain("$400.00")
    expect(near[0].textContent).toContain("$38.60 left")

    expect(find('[data-test="page-header"]')?.textContent).toContain(
      "1 near the cap"
    )
  })

  it("resolves each model role to a physical model on a named endpoint", async () => {
    await screenReady()

    const routing = find('[data-test="models-routing"]')!
    // The lead's four duties are four rows, because they do not all resolve the
    // same way — which is the point of routing by role rather than by vendor.
    for (const duty of ["plan", "contract", "review", "repair"]) {
      expect(routing.textContent).toContain(duty)
    }
    expect(routing.textContent).toContain("worker")
    expect(routing.textContent).toContain("lead-xl-2")
    expect(routing.textContent).toContain("provider-A")
  })

  it("lists a self-hosted url as an ordinary endpoint", async () => {
    await screenReady()

    const endpoints = find('[data-test="models-endpoints"]')!
    expect(endpoints.textContent).toContain(
      "http://vllm.comuki.internal:8000/v1"
    )
    expect(endpoints.textContent).toContain("openai")
    expect(endpoints.textContent).toContain("anthropic")
  })
})

describe("the acts, and who may perform them", () => {
  it("asks before revoking, and says what revoking costs mid-run", async () => {
    await screenReady()

    expect(find('[data-test="confirm-dialog"]')).toBeNull()
    fireEvent.click(all('[data-test="key-revoke"]')[0])

    await waitFor(() =>
      expect(find('[data-test="confirm-dialog"]')).not.toBeNull()
    )
    const dialog = find('[data-test="confirm-dialog"]')!
    expect(dialog.textContent).toContain("Revoke this key?")
    expect(dialog.textContent).toContain("loses it with its lease, mid-run")
  })

  it("leaves the key alone when the confirm is refused", async () => {
    await screenReady()

    const before = all(
      '[data-test="key-state-badge"][data-state="revoked"]'
    ).length
    fireEvent.click(all('[data-test="key-revoke"]')[0])
    await waitFor(() =>
      expect(find('[data-test="confirm-dialog-cancel"]')).not.toBeNull()
    )
    fireEvent.click(find('[data-test="confirm-dialog-cancel"]') as HTMLElement)

    expect(
      all('[data-test="key-state-badge"][data-state="revoked"]').length
    ).toBe(before)
  })

  it("revokes for good once the confirm is taken, and it survives the refetch", async () => {
    // The store, not an optimistic write: a query whose `queryFn` returns a
    // module constant reverts this about two hundred milliseconds later.
    await screenReady()

    const before = all(
      '[data-test="key-state-badge"][data-state="revoked"]'
    ).length
    fireEvent.click(all('[data-test="key-revoke"]')[0])
    await waitFor(() =>
      expect(find('[data-test="confirm-dialog-confirm"]')).not.toBeNull()
    )
    fireEvent.click(find('[data-test="confirm-dialog-confirm"]') as HTMLElement)

    await waitFor(() => {
      expect(
        all('[data-test="key-state-badge"][data-state="revoked"]').length
      ).toBe(before + 1)
    })
  })

  it("turns the proxy on without a confirm, and the caps start being enforced", async () => {
    // Turning it on adds enforcement; turning it off removes it. Only the
    // second one is a thing to be asked about.
    await screenReady()

    fireEvent.click(find('[data-test="proxy-toggle"]') as HTMLElement)

    await waitFor(() => {
      expect(
        find('[data-test="proxy-panel"]')?.getAttribute("data-enabled")
      ).toBe("")
    })
    expect(find('[data-test="confirm-dialog"]')).toBeNull()
    await waitFor(() => {
      expect(
        all('[data-test="budget-meter"]').every(
          (meter) => meter.getAttribute("data-enforced") === ""
        )
      ).toBe(true)
    })
  })

  it("leaves a denied act in the document, explained rather than disabled", async () => {
    // `member` holds no platform role that grants `models.manage`. The controls
    // stay where they were and say what is missing — a `disabled` control fires
    // no pointer events, so its tooltip would be unreachable.
    await screenReady(["member"])

    const toggle = find('[data-test="proxy-toggle"]') as HTMLElement
    expect(document.body.contains(toggle)).toBe(true)
    expect(toggle.getAttribute("aria-disabled")).toBe("true")
    expect(toggle.getAttribute("disabled")).toBeNull()
    expect(toggle.getAttribute("data-denied")).toBe(
      "needs operator or platform-admin"
    )

    const revoke = all('[data-test="key-revoke"]')[0] as HTMLElement
    expect(revoke.getAttribute("aria-disabled")).toBe("true")
    expect(revoke.getAttribute("data-denied")).toBe(
      "needs operator or platform-admin"
    )
  })

  it("swallows a denied click instead of opening the confirm", async () => {
    await screenReady(["member"])

    fireEvent.click(all('[data-test="key-revoke"]')[0])

    expect(find('[data-test="confirm-dialog"]')).toBeNull()
  })

  it("offers no revoke on a key that has already stopped", async () => {
    // The act does not exist for an expired or revoked row, which is a
    // different thing from a role being refused it — and only the second one
    // owes anybody a sentence.
    await screenReady()

    const rows = all('[data-test="data-table-row"]')
    const revokes = all('[data-test="key-revoke"]')
    expect(revokes.length).toBeLessThan(rows.length)
    expect(revokes.length).toBe(3)
  })
})
