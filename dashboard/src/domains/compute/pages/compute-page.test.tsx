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
import { resetSeedCompute } from "@/shared/api/mock/compute.store"
import { TestSession } from "@/shared/session/test-session"
import type { Role } from "@/shared/session"

import { ComputePage } from "./compute-page"

/* The screen serves the mock seeds, and whether it does is normally an
   environment variable that is not committed. Pinning it here makes this a test
   of the screen rather than of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws when every element it measures is zero — which, in jsdom, they all
   are. This screen has no pane group of its own; the shell's rail is one, and
   that is enough to throw. Stubbing it costs this test nothing it could have
   had: jsdom computes no layout, so nothing here was ever going to check one.
   The height chain is hand-traced in `compute-page.module.css`. */
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
    value: 320,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1400,
  })
})

beforeEach(() => {
  resetSeedCompute()
})

const rootRoute = createRootRoute({ component: ComputePage })
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

/** The screen once its one query has answered — both tables and the cards. */
async function screenReady(roles: Role[] = ["platform-admin"]) {
  renderScreen(roles)
  await waitFor(() => expect(all('[data-test="data-table"]')).toHaveLength(2))
}

describe("the compute registry, end to end over the seeds", () => {
  it("draws all three sections rather than a blank strip", async () => {
    // The failure this project has actually shipped from this shape: every gate
    // green on a screen that draws nothing. jsdom cannot see layout, so what is
    // asserted is that rows and cards exist at all once the port has a depth.
    await screenReady()

    expect(find('[data-test="compute-providers"]')).not.toBeNull()
    expect(find('[data-test="compute-pools"]')).not.toBeNull()
    expect(find('[data-test="compute-versions"]')).not.toBeNull()
    expect(all('[data-test="capacity-card"]').length).toBe(5)
    expect(all('[data-test="data-table-row"]').length).toBeGreaterThan(0)
    expect(find('[data-test="data-table-empty"]')).toBeNull()
  })

  it("opens on the pool that is about to refuse work", async () => {
    await screenReady()

    // Tightest first: the pool somebody came here about should not be third in
    // a list sorted by project name.
    const first = all('[data-test="capacity-card"]')[0]
    expect(first.getAttribute("data-binding")).toBe("quota")
    expect(
      first.querySelector('[data-test="capacity-room"]')?.textContent
    ).toBe("0 slots free")
  })

  it("names the binding ceiling and what the other one had spare", async () => {
    // The reading the screen exists for: the quota is full and the cluster is
    // not, so raising nodes would buy nothing and the operator can see that
    // without subtracting two pairs of numbers.
    await screenReady()

    const sentences = all('[data-test="capacity-binding"]').map(
      (node) => node.textContent
    )
    expect(sentences[0]).toBe(
      "quota is the ceiling — nothing can start, and the cluster still has 65 slots free"
    )
    // And the mirror case is on the same screen, so the two readings are
    // visibly different rather than one being the only shape ever shown.
    expect(
      sentences.some((text) => text?.startsWith("the cluster is the ceiling"))
    ).toBe(true)
  })

  it("reads min idle 0 as a configuration rather than as an empty pool", async () => {
    // Atlas keeps no warm containers on purpose. Without this sentence the row
    // is a pool with nothing in it, which reads as an outage.
    await screenReady()

    const knobs = all('[data-test="capacity-knobs"]').map(
      (node) => node.textContent
    )
    expect(
      knobs.some((text) => text?.includes("min idle 0 — create-per-task"))
    ).toBe(true)
    expect(
      knobs.some((text) => text?.includes("min idle 2 · max idle 6"))
    ).toBe(true)
  })

  it("calls out idle workers stranded on a label nothing matches", async () => {
    await screenReady()

    const stranded = all('[data-test="version-idle"][data-stranded]')
    expect(stranded).toHaveLength(2)
    expect(
      stranded
        .map((node) => Number(node.textContent))
        .reduce((a, b) => a + b, 0)
    ).toBe(7)

    // And the row says *which* half of the label moved — the one on the target
    // image with a moved profiles ref is the case nobody expects.
    const table = find('[data-test="compute-versions"]')
    expect(table?.textContent).toContain("a release behind on the image")
    expect(table?.textContent).toContain("same image, the profiles ref moved")

    // The header carries the same figure, so the screen agrees with itself.
    expect(find('[data-test="page-header"]')?.textContent).toContain(
      "7 idle on a label nothing matches"
    )
  })

  it("keeps a provider that did not answer out of the capacity arithmetic", async () => {
    await screenReady()

    // `no answer`, never `0` — a full cluster and a silent one are acted on
    // differently, and the registry must not spell them the same way.
    expect(find('[data-test="compute-providers"]')?.textContent).toContain(
      "no answer"
    )

    // Its pool is still up, and its card refuses to invent a ceiling. It sorts
    // last, because a provider that went quiet is not a pool about to run out.
    const cards = all('[data-test="capacity-card"]')
    const unknown = cards.filter(
      (card) => card.getAttribute("data-binding") === "unknown"
    )
    expect(unknown).toHaveLength(1)
    expect(unknown[0]).toBe(cards[cards.length - 1])
    expect(
      unknown[0].querySelector('[data-test="capacity-binding"]')?.textContent
    ).toContain("capacity api did not answer")
  })
})

describe("the acts, and who may perform them", () => {
  it("hands new starts to another provider and keeps it after the refetch", async () => {
    // The store, not an optimistic write: a query whose `queryFn` returns a
    // module constant reverts this about two hundred milliseconds later.
    await screenReady()

    fireEvent.click(all('[data-test="provider-take-work"]')[0])

    await waitFor(() => {
      const providers = find('[data-test="compute-providers"]')
      expect(providers?.textContent).toContain("draining")
    })
  })

  it("asks before tearing down containers", async () => {
    await screenReady()

    expect(find('[data-test="confirm-dialog"]')).toBeNull()
    fireEvent.click(all('[data-test="version-retire"]')[0])

    await waitFor(() =>
      expect(find('[data-test="confirm-dialog"]')).not.toBeNull()
    )
    const dialog = find('[data-test="confirm-dialog"]')!
    expect(dialog.textContent).toContain(
      "Retire the idle workers on this label?"
    )
    // The confirm carries the fact the button cannot: busy containers survive.
    expect(dialog.textContent).toContain("still holding a lease keep running")
  })

  it("retires the idle containers once the confirm is taken", async () => {
    await screenReady()

    fireEvent.click(all('[data-test="version-retire"]')[0])
    await waitFor(() =>
      expect(find('[data-test="confirm-dialog-confirm"]')).not.toBeNull()
    )
    fireEvent.click(find('[data-test="confirm-dialog-confirm"]') as HTMLElement)

    await waitFor(() => {
      expect(all('[data-test="version-idle"][data-stranded]')).toHaveLength(1)
    })
  })

  it("leaves a denied act in the document, explained rather than disabled", async () => {
    // `member` holds no platform role that grants `compute.manage`. The button
    // stays where it was, at the same size, and says what is missing — a
    // `disabled` control fires no pointer events, so its tooltip would be
    // unreachable and the operator would get a dead grey shape and no reason.
    await screenReady(["member"])

    const take = all('[data-test="provider-take-work"]')[0] as HTMLElement
    expect(document.body.contains(take)).toBe(true)
    expect(take.getAttribute("aria-disabled")).toBe("true")
    expect(take.getAttribute("disabled")).toBeNull()
    expect(take.getAttribute("data-denied")).toBe("needs operator or platform-admin")

    const retire = all('[data-test="version-retire"]')[0] as HTMLElement
    expect(retire.getAttribute("aria-disabled")).toBe("true")
    expect(retire.getAttribute("data-denied")).toBe(
      "needs operator or platform-admin"
    )
  })

  it("swallows a denied click instead of opening the confirm", async () => {
    await screenReady(["member"])

    fireEvent.click(all('[data-test="version-retire"]')[0])

    expect(find('[data-test="confirm-dialog"]')).toBeNull()
  })

  it("gives an operator the acts, because compute is platform ops", async () => {
    await screenReady(["operator"])

    const take = all('[data-test="provider-take-work"]')[0] as HTMLElement
    expect(take.getAttribute("aria-disabled")).toBeNull()
  })
})
