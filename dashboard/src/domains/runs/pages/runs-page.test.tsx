import { createContext, useContext, useState } from "react"
import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { cleanup, render, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeAll, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { TestSession } from "@/shared/session/test-session"

import { RunsPage } from "./runs-page"

/* The duty list, asked the one question this change added: does the value the
   URL carries actually land in the filter, and does typing in the filter come
   back out? Both sides of that seam are covered elsewhere — the palette builds
   the address in `app/search` and the route reads it there too. This is the
   middle, and it is the half that makes the hand-off true rather than merely
   well-intentioned. */

vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws `No layout data found for index 0` when every element it measures is
   zero — which, in jsdom, they all are. Stubbed to bare boxes exactly as
   `queue-page.test.tsx` stubs it, and for the same reason: jsdom computes no
   layout, so nothing in this file was ever going to check one. */
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
  Object.defineProperty(HTMLElement.prototype, "offsetHeight", {
    configurable: true,
    value: 320,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1400,
  })
})

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren([
  ...[
    "/",
    "/tasks",
    "/runs",
    "/queue",
    "/approvals",
    "/cost",
    "/knowledge",
    "/verify",
    "/sources",
    "/settings",
    "/projects",
    "/identity",
    "/compute",
    "/models",
    "/observability",
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  ),
  createRoute({
    getParentRoute: () => rootRoute,
    path: "/runs/$runId",
    component: blank,
  }),
])

function renderScreen(node: ReactNode) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={["platform-admin"]}>
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          <SlotContext value={node}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const search = () =>
  document.querySelector<HTMLInputElement>('[data-test="data-table-search"]')

/** How many rows the toolbar says are showing. */
function shown(): number {
  const label = Array.from(document.querySelectorAll("span"))
    .map((node) => node.textContent ?? "")
    .find((text) => /^\d+ shown$/.test(text))
  return Number((label ?? "0").split(" ")[0])
}

/** The screen once its query has answered and the toolbar exists. */
async function ready(node: ReactNode) {
  renderScreen(node)
  await waitFor(() => expect(search()).not.toBeNull())
  await waitFor(() => expect(shown()).toBeGreaterThan(0))
}

/** The route's half of the contract, in miniature: a value that writes back. */
function AsRoute({ onWrite }: { onWrite?: (next: string) => void }) {
  const [value, setValue] = useState("")

  return (
    <RunsPage
      search={value}
      onSearchChange={(next) => {
        setValue(next)
        onWrite?.(next)
      }}
    />
  )
}

describe("the duty list's search filter, held in the URL", () => {
  it("lands already narrowed when the route hands it a value", async () => {
    await ready(<RunsPage search="web-app" onSearchChange={() => {}} />)

    expect(search()?.value).toBe("web-app")
    const narrowed = shown()

    cleanup()

    // The same screen with nothing narrowing it is strictly longer, which is
    // the whole point of the hand-off: the address did the filtering.
    await ready(<RunsPage search="" onSearchChange={() => {}} />)
    expect(shown()).toBeGreaterThan(narrowed)
  })

  it("writes every keystroke back so the address can be rewritten", async () => {
    const user = userEvent.setup()
    const onWrite = vi.fn()
    await ready(<AsRoute onWrite={onWrite} />)

    await user.type(search()!, "web")

    expect(onWrite).toHaveBeenCalledTimes(3)
    expect(onWrite.mock.calls.at(-1)?.[0]).toBe("web")
    expect(search()?.value).toBe("web")
  })

  it("narrows the list from the value it wrote, not from a second copy", async () => {
    const user = userEvent.setup()
    await ready(<AsRoute />)

    const all = shown()
    await user.type(search()!, "web-app")

    await waitFor(() => expect(shown()).toBeLessThan(all))
    expect(shown()).toBeGreaterThan(0)
  })

  it("keeps the value itself when nobody is driving it", async () => {
    const user = userEvent.setup()
    await ready(<RunsPage />)

    const all = shown()
    await user.type(search()!, "web-app")

    // Uncontrolled is the behaviour the screen had before the route existed,
    // unchanged: it narrows itself and nothing outside it hears.
    await waitFor(() => expect(shown()).toBeLessThan(all))
    expect(search()?.value).toBe("web-app")
  })

  it("leaves the board's own filter alone when the search changes", async () => {
    const user = userEvent.setup()
    await ready(<AsRoute />)

    // The two filters take different routes into the same bag now — one
    // through the URL, one through local state — and the risk that creates is
    // exactly this: one of them dropping the other on the way in. Clicking a
    // stage writes `profile`; typing writes the promoted search; both have to
    // survive the other.
    const node = document.querySelector<HTMLElement>('[data-test="river-node"]')
    expect(node).not.toBeNull()

    await user.click(node!)
    expect(node!.getAttribute("aria-pressed")).toBe("true")

    await user.type(search()!, "w")

    expect(search()?.value).toBe("w")
    expect(node!.getAttribute("aria-pressed")).toBe("true")
    // The board's filter is still on the row, said out loud as a chip.
    expect(
      document.querySelector('[data-test="data-table-chips"]')
    ).not.toBeNull()
  })
})
