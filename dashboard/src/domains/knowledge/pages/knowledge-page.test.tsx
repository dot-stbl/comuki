import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  Outlet,
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { Route as KnowledgeRoute } from "@/routes/knowledge"
import { Route as VerifyRoute } from "@/routes/verify"
import type { KnowledgeTab } from "@/domains/knowledge"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { resetSeedVerify } from "@/shared/api/mock/verify.store"

import { KnowledgePage } from "./knowledge-page"

/* The screen serves the mock seeds, and whether it does is normally an
   environment variable that is not committed. Pinning it here makes this a test
   of the screen rather than of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws when every element it measures is zero — which, in jsdom, they all
   are. This screen has no pane group of its own; the shell's rail is one, and
   that is enough to throw. The height chain is hand-traced in
   `knowledge-page.module.css` instead, because jsdom computes no layout and
   nothing here was ever going to check one. */
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
   and jsdom has neither — without these the body renders no rows and an
   assertion about the harness would pass against an empty frame. Same stubs as
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

afterEach(() => {
  // The gate tab flips a mutable store; a test that flipped it must not leave
  // it flipped for the one after.
  resetSeedVerify()
  // The shell persists the rail's layout per pane group, and a layout written
  // by a previous mount is meaningless to the next one in a document that
  // measures nothing.
  localStorage.clear()
})

/* The product marks its own elements with `data-test`, not `data-testid`. */
const find = (selector: string) => document.querySelector(selector)
const all = (selector: string) =>
  Array.from(document.querySelectorAll(selector))
const entries = () => all('[data-test="knowledge-entry"]')
const panels = () => all('[data-test="verify-project"]')
const toggles = () =>
  all('[data-test="verify-enabled"]') as HTMLInputElement[]
const sheet = () => find('[data-test="knowledge-sheet"]')
const searchBox = () =>
  find('[data-test="knowledge-search"]') as HTMLInputElement

const blank = () => null

interface ScreenOptions {
  focus?: string
  tab?: KnowledgeTab
  roles?: Role[]
  projectRoles?: Record<string, Role[]>
}

function renderScreen({
  focus,
  tab = "library",
  roles = ["member"],
  projectRoles = {},
}: ScreenOptions = {}) {
  const rootRoute = createRootRoute({
    component: () => (
      <KnowledgePage tab={tab} focus={focus} onTabChange={() => {}} />
    ),
  })
  const routeTree = rootRoute.addChildren(
    ["/settings", "/runs", "/knowledge"].map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    )
  )
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles} projectRoles={projectRoles}>
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

/**
 * The real route files on a real router — the redirect stub at `/verify` and
 * the search validation at `/knowledge` are route concerns, so the test mounts
 * the routes themselves rather than a copy of their wiring.
 *
 * A file route carries the linkage `createFileRoute` gave it, so mounting it
 * under a different root takes the same `update` call `routeTree.gen.ts` uses
 * to mount it under the app's.
 */
function renderRoutes(initial: string, roles: Role[]) {
  const rootRoute = createRootRoute({ component: () => <Outlet /> })
  /* The project-wide `Register` declaration pins these routes' option types
     to the generated tree, so re-parenting needs the same escape
     `routeTree.gen.ts` buys with `as any` — spelled `as never` here to stay
     lint-clean. */
  const knowledgeRoute = KnowledgeRoute.update({
    id: "/knowledge",
    path: "/knowledge",
    getParentRoute: () => rootRoute,
  } as never)
  const verifyRoute = VerifyRoute.update({
    id: "/verify",
    path: "/verify",
    getParentRoute: () => rootRoute,
  } as never)
  const routeTree = rootRoute.addChildren([knowledgeRoute, verifyRoute])
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [initial] }),
  })

  render(
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

  return router
}

async function ready(focus?: string) {
  renderScreen({ focus })
  await waitFor(() => expect(entries().length).toBeGreaterThan(0))
}

describe("the knowledge screen, end to end over the seeds", () => {
  it("draws all three regions rather than a blank strip", async () => {
    // The failure this project has actually shipped from this shape: every gate
    // green on a screen that draws nothing. jsdom sees no layout, so what is
    // asserted is that each region rendered something at all.
    await ready()

    expect(find('[data-test="knowledge-revision"]')).not.toBeNull()
    expect(find('[data-test="knowledge-entries"]')).not.toBeNull()
    expect(find('[data-test="knowledge-eval"]')).not.toBeNull()
    expect(entries()).toHaveLength(7)
  })

  it("keeps the three revision readings the screen exists to state", async () => {
    await ready()

    const region = find('[data-test="knowledge-revision"]')
    expect(region?.textContent).toContain("rules@a1b9e0")
    expect(region?.textContent).toContain("sdk@2.4.1 · updated 2h ago")
    expect(region?.textContent).toContain("3 hard · 2 soft")
    expect(region?.textContent).toContain("every run pins the rule set + SDK")
  })

  it("puts the search in the header band, where it cannot scroll away", async () => {
    await ready()

    const band = find('[data-test="page-header-filters"]')
    expect(band?.querySelector('[data-test="knowledge-search"]')).not.toBeNull()
  })

  it("narrows the list from the field and says so when nothing matches", async () => {
    await ready()

    fireEvent.change(searchBox(), { target: { value: "web-app" } })
    await waitFor(() => expect(entries()).toHaveLength(1))

    fireEvent.change(searchBox(), { target: { value: "nothing matches this" } })
    await waitFor(() =>
      expect(find('[data-test="knowledge-empty"]')).not.toBeNull()
    )
    expect(screen.getByText("No matches")).toBeTruthy()
  })

  it("arrives narrowed when the address bar carried a query", async () => {
    // `/knowledge?q=web-app` — the link somebody pasted into a review.
    await ready("web-app")

    expect(entries()).toHaveLength(1)
    // And it is the operator's from that moment: the value is in the field
    // they would clear it from, not applied invisibly behind it.
    expect(searchBox().value).toBe("web-app")
  })

  it("opens the entry sheet from a row and closes it again", async () => {
    await ready()

    expect(sheet()).toBeNull()
    fireEvent.click(entries()[0])

    await waitFor(() => expect(sheet()).not.toBeNull())
    expect(sheet()?.textContent).toContain("api-errors")

    fireEvent.click(screen.getByRole("button", { name: "Close the entry" }))
    await waitFor(() => expect(sheet()).toBeNull())
  })
})

/**
 * The gate, folded in from the screen that used to stand at `/verify`.
 *
 * Mounted the way the route mounts the page, with the tab the address bar
 * named. jsdom computes no layout, so this cannot prove the tab strip is not a
 * blank sliver — what it proves is that the fold kept what the old screen
 * existed to say: one gate per project, each naming its own file, one switch
 * per gate that survives the refetch that follows it, and a denial that
 * explains itself rather than disappearing.
 */
describe("the folded gate tab", () => {
  it("is hidden from a session that cannot see it, while the library stands", async () => {
    // A plain member holds `knowledge.view` and not `verify.view`: the door
    // opens, the folded section does not — hidden, never disabled.
    await ready()

    expect(find('[data-test="tab-library"]')).not.toBeNull()
    expect(find('[data-test="tab-gate"]')).toBeNull()
    expect(panels()).toHaveLength(0)
  })

  it("falls back to the library when the address bar names the gate for such a session", async () => {
    // An old `/verify` link in the hands of a member resolves to
    // `/knowledge?tab=gate`; the tab they cannot have is not rendered, so the
    // strip must not point at a panel that does not exist.
    renderScreen({ tab: "gate" })
    await waitFor(() => expect(entries().length).toBeGreaterThan(0))

    expect(find('[data-test="tab-gate"]')).toBeNull()
    expect(
      (find('[data-test="tab-library"]') as HTMLElement).hasAttribute(
        "data-selected"
      )
    ).toBe(true)
    expect(panels()).toHaveLength(0)
  })

  it("carries one gate per project, each naming its own file", async () => {
    renderScreen({ tab: "gate", roles: ["platform-admin"] })
    await waitFor(() => expect(panels().length).toBeGreaterThan(0))

    expect(
      (find('[data-test="tab-gate"]') as HTMLElement).hasAttribute(
        "data-selected"
      )
    ).toBe(true)
    // The library's list is not mounted under the gate's panels: the two
    // questions share a door, not a page.
    expect(entries()).toHaveLength(0)

    expect(panels()).toHaveLength(3)
    for (const panel of panels()) {
      const path = panel.querySelector('[data-test="verify-source-path"]')
      // Every section, without exception: a screen that named the file for two
      // projects out of three would be worse than one that named none.
      expect(path?.textContent).toMatch(/ @ .+ · .+/)
      expect(
        (
          panel.querySelector(
            '[data-test="verify-source-link"]'
          ) as HTMLAnchorElement
        ).getAttribute("href")
      ).toMatch(/^https:\/\//)
    }
  })

  it("says up front that editing a command is a commit", async () => {
    renderScreen({ tab: "gate", roles: ["platform-admin"] })
    await waitFor(() => expect(panels().length).toBeGreaterThan(0))

    expect(
      screen.getByText(
        /Editing a command means editing the file; every section below says exactly where its file is\./
      )
    ).toBeTruthy()
  })

  it("keeps a flipped gate across the refetch that follows it", async () => {
    renderScreen({ tab: "gate", roles: ["platform-admin"] })
    await waitFor(() => expect(panels().length).toBeGreaterThan(0))

    const first = toggles()[0]
    expect(first.checked).toBe(true)

    fireEvent.click(first)

    // Off, and still off after the invalidate — which is the whole point of the
    // mutable store. A `queryFn` mapping a module constant would flip it back
    // about two hundred milliseconds later.
    await waitFor(() => expect(toggles()[0].checked).toBe(false), {
      timeout: 3000,
    })
  })

  it("refuses the switch on a project this session only watches", async () => {
    // `verify.view` opens the tab because this person administers one project;
    // `settings.live` is asked per project, so the other two explain themselves
    // rather than disappearing.
    renderScreen({
      tab: "gate",
      roles: ["viewer"],
      projectRoles: { p_comuki: ["project-admin"] },
    })
    await waitFor(() => expect(panels().length).toBeGreaterThan(0))

    const denials = toggles().map((toggle) =>
      toggle.getAttribute("aria-disabled")
    )
    expect(denials).toContain("true")
    expect(denials).toContain(null)

    for (const toggle of toggles()) {
      // Never `disabled` for a denial: the sentence has to stay reachable.
      expect(toggle.hasAttribute("disabled")).toBe(false)
    }
  })
})

describe("the route that used to be a screen", () => {
  it("opens `/verify` on the knowledge gate tab", async () => {
    const router = renderRoutes("/verify", ["platform-admin"])

    await waitFor(() =>
      expect(router.state.location.pathname).toBe("/knowledge")
    )
    expect(router.state.location.search).toMatchObject({ tab: "gate" })

    await waitFor(() => expect(panels()).toHaveLength(3))
  })

  it("reads an unknown tab as the first section", async () => {
    renderRoutes("/knowledge?tab=nonsense", ["member"])

    await waitFor(() => expect(entries().length).toBeGreaterThan(0))

    // The unknown word never reached the strip as a selection: the library
    // stands selected, the section it names for this session is not offered,
    // and the URL's stray word drew no panel of its own.
    expect(
      (find('[data-test="tab-library"]') as HTMLElement).hasAttribute(
        "data-selected"
      )
    ).toBe(true)
    expect(find('[data-test="tab-gate"]')).toBeNull()
    expect(panels()).toHaveLength(0)
  })
})
