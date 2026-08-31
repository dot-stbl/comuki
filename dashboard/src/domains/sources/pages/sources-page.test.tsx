import { createContext, useContext } from "react"
import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { afterEach, beforeAll, describe, expect, it } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { SourcesPage } from "@/domains/sources/pages/sources-page"
import { resetSeedSources } from "@/shared/api/mock/sources.store"
import { PROJECTS_SEED } from "@/shared/api/mock/session.seed"
import { SessionProvider, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

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
    value: 1200,
  })
})

afterEach(() => {
  resetSeedSources()
  // `SplitPane` persists the rail's layout per pane group, and a layout written
  // by a previous mount is meaningless to the next one in a document that
  // measures nothing. Left behind, it makes the second render in a file throw
  // where the first did not.
  localStorage.clear()
})

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  [
    "/",
    "/runs",
    "/tasks",
    "/settings",
    "/sources",
    // The three screens the section grew when its dialogs became pages. The
    // header control and two of the row's four buttons are navigation now, so
    // a router that does not know these paths cannot render the list at all.
    "/sources/new",
    "/sources/$sourceId",
    "/sources/$sourceId/ticket/new",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

/**
 * The screen as the route mounts it, on the *seeded* projects rather than the
 * test session's two — the connections live on `p_comuki`, `p_plexor` and
 * `p_atlas`, and the point of the screen is that one list mixes them.
 */
function renderPage(
  roles: Role[],
  projectRoles: Record<string, Role[]> = {},
  focus?: string
) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/sources"] }),
  })

  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles} projectRoles={projectRoles}>
        <QueryClientProvider client={new QueryClient()}>
          <SlotContext value={<SourcesPage focus={focus} />}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )

  return router
}

const find = (selector: string) => document.querySelector(selector)

/**
 * The whole screen, mounted the way the route mounts it.
 *
 * jsdom computes no layout, so this cannot prove the page is not a blank strip
 * — that is what the hand-traced height chain in the stylesheet is for. What it
 * proves is the loop nothing else does: a decision taken on a row survives the
 * refetch that follows it, which is the difference between a screen that works
 * and one whose optimistic write vanishes two hundred milliseconds later.
 */
describe("the sources screen", () => {
  it("names itself once and lists the seeded connections", async () => {
    renderPage(["platform-admin"])

    await waitFor(() =>
      expect(find("[data-test='connections-panel']")).not.toBeNull()
    )

    expect((await screen.findByRole("heading", { level: 1 })).textContent).toBe(
      "Sources"
    )
    expect(screen.getByText("comuki/web-app")).toBeTruthy()
    expect(screen.getAllByText("native intake").length).toBeGreaterThan(0)
  })

  it("spells Connect a source as a destination when the role has it", async () => {
    renderPage(["platform-admin"])

    await waitFor(() =>
      expect(find("[data-test='connect-source']")).not.toBeNull()
    )

    // Allowed, the act is navigation and it is spelled as navigation: an anchor
    // that can be opened in a tab, copied into a ticket, and traversed by
    // anything that follows links.
    const connect = find("[data-test='connect-source']") as HTMLAnchorElement
    expect(connect.tagName.toLowerCase()).toBe("a")
    expect(connect.getAttribute("href")).toBe("/sources/new")
    expect(connect.getAttribute("aria-label")).toBe("Connect a source")
  })

  it("keeps Connect a source visible and explained for a role without it", async () => {
    renderPage(["member"])

    await waitFor(() =>
      expect(find("[data-test='connect-source']")).not.toBeNull()
    )

    const connect = find("[data-test='connect-source']") as HTMLButtonElement
    // Asked without a project: "may this person connect one *somewhere*?" —
    // and this one may not, anywhere. Hiding it would leave them wondering
    // whether the product has the feature at all.
    expect(connect.getAttribute("aria-disabled")).toBe("true")
    // Icon-only in a kit tooltip now, so the sentence arrives there and the
    // button drops its native title rather than delivering it twice. The name
    // is still the words the glyph replaced.
    expect(connect.getAttribute("aria-label")).toBe("Connect a source")
    expect(connect.getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin"
    )
  })

  it("keeps a disconnect across the refetch that follows it", async () => {
    renderPage(["platform-admin"])

    await waitFor(() =>
      expect(find("[data-test='connections-panel']")).not.toBeNull()
    )

    const row = await screen.findByRole("button", {
      name: "Disconnect comuki/web-app",
    })
    fireEvent.click(row)

    const confirmButton = await waitFor(() => {
      const node = find("[data-test='confirm-dialog-confirm']")
      expect(node).not.toBeNull()
      return node as HTMLButtonElement
    })

    // The dialog names the project, because this list mixes them and cutting a
    // credential is the last moment to notice it is the wrong one's.
    expect(find("[data-test='confirm-dialog']")?.textContent).toContain(
      "comuki"
    )

    fireEvent.click(confirmButton)

    // Gone, and still gone after the invalidate — which is the whole point of
    // the mutable store. A `queryFn` mapping a module constant would put this
    // row straight back.
    await waitFor(
      () =>
        expect(
          screen.queryByRole("button", { name: "Disconnect comuki/web-app" })
        ).toBeNull(),
      { timeout: 3000 }
    )
  })
})

/**
 * `?q=` arrives from somewhere else, and the somewhere else is the argument.
 *
 * A project's own page hands its sources off as `/sources?q=<project slug>`,
 * and the global palette resolves a project handle to the same address. Both
 * only work because the promoted filter matches the **project key** as well as
 * the three fields its placeholder names — a destination that cannot receive
 * what it is sent lands the operator on an empty screen, which is worse than
 * not resolving at all. See the contract at the top of `app/search/shapes.ts`.
 *
 * These mount on the *seeded* projects rather than the test session's two,
 * because a project key is exactly what is under test and `p_plexor` has to
 * resolve to `plexor` for the question to mean anything.
 */
describe("the list receives what another screen sends it", () => {
  function renderWithSeededProjects(focus?: string) {
    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: ["/sources"] }),
    })

    render(
      <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
        <SessionProvider
          user={{
            id: "u_test",
            name: "Test User",
            email: "test@comuki.local",
            platformRoles: ["platform-admin"],
            projectRoles: {},
          }}
          projects={PROJECTS_SEED}
        >
          <QueryClientProvider client={new QueryClient()}>
            <SlotContext value={<SourcesPage focus={focus} />}>
              <RouterProvider router={router} />
            </SlotContext>
          </QueryClientProvider>
        </SessionProvider>
      </ThemeProvider>
    )
  }

  it("seeds the toolbar's own filter rather than narrowing behind its back", async () => {
    renderWithSeededProjects("comuki/web-app")

    await waitFor(() =>
      expect(find("[data-test='connections-panel']")).not.toBeNull()
    )

    // The narrowing is *visible*, sitting in the control that did it and one
    // click from being cleared. Coupling two surfaces invisibly is the one
    // thing this product's tables are not allowed to do.
    const box = screen.getByPlaceholderText(
      /filter source, account, host/i
    ) as HTMLInputElement
    expect(box.value).toBe("comuki/web-app")

    await waitFor(() => expect(screen.getByText("comuki/web-app")).toBeTruthy())
    expect(screen.queryByText("plexor/identity-svc")).toBeNull()
  })

  it("matches the project key, because that is what a project page sends", async () => {
    // `plexor` appears in one connection's name by luck and in the *key* of two
    // rows by construction — the self-hosted gitlab and that project's native
    // intake. Native is the proof: nothing in its name, account, host or reason
    // says `plexor`, so it is only on this list because the key is in the
    // haystack.
    renderWithSeededProjects("plexor")

    await waitFor(() =>
      expect(find("[data-test='connections-panel']")).not.toBeNull()
    )

    await waitFor(() =>
      expect(screen.getByText("plexor/identity-svc")).toBeTruthy()
    )

    // Two rows and exactly two: the gitlab connection, whose name says
    // `plexor`, and that project's native intake, whose name, account, host and
    // reason say nothing of the kind. The second one is the assertion.
    const rows = [
      ...document.querySelectorAll('[data-test="source-link"]'),
    ].map((link) => link.textContent)
    expect(rows).toEqual(["plexor/identity-svc", "native intake"])
    expect(rows).not.toContain("comuki/web-app")
  })

  it("leaves the list whole when nothing was sent", async () => {
    renderWithSeededProjects()

    await waitFor(() =>
      expect(find("[data-test='connections-panel']")).not.toBeNull()
    )

    const box = screen.getByPlaceholderText(
      /filter source, account, host/i
    ) as HTMLInputElement
    expect(box.value).toBe("")
    await waitFor(() => expect(screen.getByText("comuki/web-app")).toBeTruthy())
    expect(screen.getByText("plexor/identity-svc")).toBeTruthy()
  })
})
