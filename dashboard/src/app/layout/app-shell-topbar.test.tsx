import { createContext, useContext } from "react"
import type { ReactNode } from "react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

import { AppShellTopbar } from "./app-shell-topbar"

/* The bar on its own, without the shell around it: the rail is a pane group
   and jsdom measures nothing, so mounting the whole shell to look at four
   controls buys a `useLayoutEffect` throw and nothing else. */

/* The repository address is configuration, so both of its states have to be
   reachable from a test — the deployment that has one and the deployment that
   has deliberately taken it out. A hoisted holder, mutated per case, because
   `vi.mock` factories run before anything in the file body. */
const envState = vi.hoisted(() => ({
  useMock: true,
  repoUrl: "https://github.com/dot-stbl/comuki" as string | null,
}))

vi.mock("@/shared/config/env", () => ({ env: envState }))

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/tasks", "/runs", "/queue"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function renderBar(roles?: Role[]) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles}>
        <SlotContext value={<AppShellTopbar />}>
          <RouterProvider router={router} />
        </SlotContext>
      </TestSession>
    </ThemeProvider>
  )
}

const find = (selector: string) =>
  document.querySelector<HTMLElement>(selector)

beforeEach(() => {
  envState.repoUrl = "https://github.com/dot-stbl/comuki"
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe("the mark, alone", () => {
  it("keeps the home link and carries no wordmark with it", async () => {
    renderBar(["member"])

    const home = await screen.findByRole("link", { name: "Comuki — home" })
    // The mark is the entire lockup. Nothing in the link spells the name a
    // second time — not visibly, and not behind an `aria-hidden` either, which
    // is where the reveal used to live.
    expect(home.textContent).toBe("")
    expect(home.querySelector("svg")).not.toBeNull()
    expect(document.body.textContent).not.toContain("Comuki")
  })
})

describe("the repository link", () => {
  it("points at the platform's own source, in a new tab, safely", async () => {
    renderBar(["member"])

    const link = (await screen.findByRole("link", {
      name: /github/i,
    })) as HTMLAnchorElement

    expect(link.getAttribute("href")).toBe("https://github.com/dot-stbl/comuki")
    expect(link.getAttribute("target")).toBe("_blank")
    // Both halves, always: `noopener` is what stops the opened page reaching
    // back through `window.opener`.
    expect(link.getAttribute("rel")).toBe("noreferrer noopener")
  })

  it("names what it does rather than what it draws", async () => {
    renderBar(["member"])

    const link = await screen.findByRole("link", { name: /github/i })

    // "github" is the mark's name, not the control's. The control says where
    // it goes and that it leaves the product.
    expect(link.getAttribute("aria-label")).toBe(
      "Comuki on GitHub — opens in a new tab"
    )
    expect(find('[data-test="repo-link"] svg')).not.toBeNull()
  })

  it("does not render at all when there is nowhere to go", async () => {
    envState.repoUrl = null
    renderBar(["member"])

    await screen.findByRole("link", { name: "Comuki — home" })
    // A mark linking nowhere is worse than no mark: it looks like a control
    // and answers to nothing.
    expect(find('[data-test="repo-link"]')).toBeNull()
  })
})

describe("the bar's controls", () => {
  it("offers the search to every role, whatever else it hides", async () => {
    renderBar(["viewer"])

    // A viewer keeps almost no rail, and can still ask where something is —
    // the palette hides the destinations, not the question.
    expect(await screen.findByRole("button", { name: /search/i })).not.toBeNull()
    expect(find('[data-test="new-run"]')).toBeNull()
  })

  it("keeps the theme control and the search beside each other", async () => {
    renderBar(["member"])

    await screen.findByRole("link", { name: "Comuki — home" })
    expect(find('[data-test="global-search"]')).not.toBeNull()
    expect(find('[data-test="theme-control"]')).not.toBeNull()
  })
})
