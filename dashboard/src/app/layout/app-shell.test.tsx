import type { ReactNode } from "react"
import { createContext, useContext } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeAll, describe, expect, it } from "vitest"

import { AppShell } from "@/app/layout/app-shell"
import { AppShellSidebar } from "@/app/layout/app-shell-sidebar"
import { productNav } from "@/app/layout/nav"
import { ThemeProvider } from "@/app/theme-provider"
import { APPROVALS_SEED } from "@/shared/api/mock"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
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
    "/tasks",
    "/runs",
    "/approvals",
    "/cost",
    "/knowledge",
    "/settings",
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function renderShell(node: ReactNode, roles?: Role[]) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles}>
        <QueryClientProvider client={new QueryClient()}>
          <SlotContext value={node}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const find = (selector: string) => document.querySelector(selector)

describe("the shell's topbar", () => {
  it("carries the theme control and the account block", async () => {
    renderShell(<AppShell>screen body</AppShell>)

    await screen.findByText("screen body")
    expect(find("[data-test='theme-control']")).not.toBeNull()
    expect(find("[data-test='rail-account']")).not.toBeNull()
  })

  // The one control in the chrome that looks like an action and is not: it is
  // a link, and it links to a screen the same role cannot open. §17 hides
  // navigation, so it goes rather than standing there explaining itself.
  it("drops New run for a role that cannot take intake", async () => {
    renderShell(<AppShell>screen body</AppShell>, ["viewer"])

    await screen.findByText("screen body")
    expect(find("[data-test='new-run']")).toBeNull()
  })

  it("keeps New run for a role that can", async () => {
    renderShell(<AppShell>screen body</AppShell>, ["member"])

    await screen.findByText("screen body")
    expect(find("[data-test='new-run']")).not.toBeNull()
  })
})

describe("the shell's rail", () => {
  it("anchors the account block to the floor, whatever the rail holds", async () => {
    renderShell(<AppShell>screen body</AppShell>, ["viewer"])

    await screen.findByText("screen body")
    // A viewer keeps almost no rail. Identity is the one thing whose position
    // must not move as sections appear and disappear with a role.
    expect(find("[data-test='rail-account']")).not.toBeNull()
  })

  it("drops the platform tier, and its divider with it", async () => {
    renderShell(<AppShell>screen body</AppShell>, ["member"])

    await screen.findByText("screen body")
    expect(screen.queryByLabelText("Platform")).toBeNull()
  })

  it("gives an operator the platform tier", async () => {
    renderShell(<AppShell>screen body</AppShell>, ["operator"])

    await screen.findByText("screen body")
    expect(screen.getByLabelText("Platform")).not.toBeNull()
  })

  it("counts the approvals row's own queue, not the waiting runs", async () => {
    // The badge once counted waiting runs — the runs screen's reading worn on
    // the wrong row. A badge answers "how is my screen", so it counts that
    // screen's own things: the undecided approvals, which is exactly what the
    // queue holds (deciding removes the card).
    renderShell(<AppShellSidebar groups={productNav} />, ["approver"])

    const item = await screen.findByRole("link", { name: /approvals/i })
    await waitFor(
      () =>
        expect(
          item.querySelector('[data-test="rail-badge"]')?.textContent
        ).toBe(String(APPROVALS_SEED.length))
    )
  })
})

/* The rail on its own, so the collapsed state can be asserted at all: the panel
   collapses by measurement and jsdom measures nothing, so the shell can never
   reach this state in a test. The prop is the same one the panel sets. */
describe("the collapsed rail", () => {
  it("keeps every item's name, and hands the icon a tooltip as well", async () => {
    const user = userEvent.setup()
    renderShell(
      <AppShellSidebar groups={productNav} collapsed />,
      ["viewer"]
    )

    // Clipped to zero width, never hidden: the name is still the link's name.
    const item = await screen.findByRole("link", { name: "Live runs" })
    // And it is no longer a native `title`, which is what the tooltip replaced.
    expect(item.getAttribute("title")).toBeNull()

    await user.tab()
    expect((await screen.findByRole("tooltip")).textContent).toBe("Live runs")
  })

  it("says nothing twice when the rail has its words back", async () => {
    const user = userEvent.setup()
    renderShell(<AppShellSidebar groups={productNav} />, ["viewer"])

    await screen.findByRole("link", { name: "Live runs" })

    await user.tab()
    expect(screen.queryByRole("tooltip")).toBeNull()
  })
})
