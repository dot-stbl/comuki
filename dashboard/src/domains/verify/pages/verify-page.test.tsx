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
import { VerifyPage } from "@/domains/verify/pages/verify-page"
import { resetSeedVerify } from "@/shared/api/mock/verify.store"
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
  resetSeedVerify()
  // `SplitPane` persists the rail's layout per pane group, and a layout written
  // by a previous mount is meaningless to the next one in a document that
  // measures nothing.
  localStorage.clear()
})

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/runs/$runId", "/tasks", "/settings", "/verify"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function renderPage(roles: Role[], projectRoles: Record<string, Role[]> = {}) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/verify"] }),
  })

  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles} projectRoles={projectRoles}>
        <QueryClientProvider client={new QueryClient()}>
          <SlotContext value={<VerifyPage />}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const panels = () => [...document.querySelectorAll('[data-test="verify-project"]')]

/**
 * The whole screen, mounted the way the route mounts it.
 *
 * jsdom computes no layout, so this cannot prove the page is not a blank strip
 * — that is the hand-traced height chain's job. What it proves is that the
 * screen says the one thing it exists to say, on every project, and that the
 * one decision it offers survives the refetch that follows it.
 */
describe("the verify screen", () => {
  it("carries one gate per project, each naming its own file", async () => {
    renderPage(["platform-admin"])

    await waitFor(() => expect(panels().length).toBeGreaterThan(0))

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
    renderPage(["platform-admin"])
    await waitFor(() => expect(panels().length).toBeGreaterThan(0))

    expect(
      screen.getByText(
        /Editing a command means editing the file; every section below says exactly where its file is\./
      )
    ).toBeTruthy()
  })

  it("keeps a flipped gate across the refetch that follows it", async () => {
    renderPage(["platform-admin"])
    await waitFor(() => expect(panels().length).toBeGreaterThan(0))

    const toggles = [
      ...document.querySelectorAll('[data-test="verify-enabled"]'),
    ] as HTMLInputElement[]
    const first = toggles[0]
    expect(first.checked).toBe(true)

    fireEvent.click(first)

    // Off, and still off after the invalidate — which is the whole point of the
    // mutable store. A `queryFn` mapping a module constant would flip it back
    // about two hundred milliseconds later.
    await waitFor(
      () =>
        expect(
          (
            document.querySelectorAll(
              '[data-test="verify-enabled"]'
            )[0] as HTMLInputElement
          ).checked
        ).toBe(false),
      { timeout: 3000 }
    )
  })

  it("refuses the switch on a project this session only watches", async () => {
    // `verify.view` opens the route because this person administers one
    // project; `settings.live` is asked per project, so the other two explain
    // themselves rather than disappearing.
    renderPage(["viewer"], { p_comuki: ["project-admin"] })
    await waitFor(() => expect(panels().length).toBeGreaterThan(0))

    const toggles = [
      ...document.querySelectorAll('[data-test="verify-enabled"]'),
    ] as HTMLInputElement[]

    const denials = toggles.map((toggle) => toggle.getAttribute("aria-disabled"))
    expect(denials).toContain("true")
    expect(denials).toContain(null)

    for (const toggle of toggles) {
      // Never `disabled` for a denial: the sentence has to stay reachable.
      expect(toggle.hasAttribute("disabled")).toBe(false)
    }
  })
})
