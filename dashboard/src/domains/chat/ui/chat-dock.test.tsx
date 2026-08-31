import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { ChatPage } from "@/domains/chat/pages/chat-page"
import { ChatDock } from "@/domains/chat/ui/chat-dock"
import { resetChatDockMemory } from "@/domains/chat/ui/chat-dock-memory"
import { resetChatSessions } from "@/shared/api/mock/chat.store"
import { PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider, type SessionUser } from "@/shared/session"

/* The screen serves the mock, and whether it does is normally an environment
   variable that is not committed. Pinned here so this is a test of the dock. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

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

  /* The sheet's pane group persists its layout, and in jsdom the layout it
     saves was measured against nothing. Restoring that on the next mount
     makes `react-resizable-panels` throw `No layout data found for index
     0` — and several cases here mount the dock more than once. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  resetChatSessions()
  // The dock's memory is the point of it — and exactly what must not leak
  // between cases: a sheet left open by one test would open on mount in the
  // next, and the focus React Aria restores is the one that was active when
  // the modal mounted, which would no longer be the trigger.
  resetChatDockMemory()
})

const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

const all = (name: string) =>
  Array.from(document.querySelectorAll<HTMLElement>(`[data-test="${name}"]`))

/** The rail links to every product screen, so a memory router must know them. */
const RAIL_PATHS = [
  "/",
  "/tasks",
  "/runs",
  "/runs/$runId",
  "/queue",
  "/approvals",
  "/cost",
  "/sources",
  "/knowledge",
  "/verify",
  "/settings",
  "/identity",
  "/compute",
  "/models",
  "/observability",
  "/components",
  "/chat/init",
]

/**
 * The tree the dock actually lives in: the dock at the root, the routed
 * screen below it — the same shape as the shell, without the shell's own pane
 * group, which jsdom measures as nothing and which has its own tests.
 *
 * `chatRoute` mounts the real `/chat` route for the same-console comparison;
 * otherwise the dock stands alone over blank routes.
 */
function mountTree(
  initial: string,
  user: SessionUser = SESSION_USER_SEED,
  chatRoute = false
) {
  const rootRoute = createRootRoute({
    component: () => (
      <>
        {/* The sentinel says the router has painted — `RouterProvider` mounts
            asynchronously, and "the trigger is not drawn" must not pass just
            because nothing has rendered yet. */}
        <span data-test="dock-tree-mounted" />
        <ChatDock />
        <Outlet />
      </>
    ),
  })
  const blank = () => null

  const routes = RAIL_PATHS.map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
  if (chatRoute) {
    routes.push(
      createRoute({
        getParentRoute: () => rootRoute,
        path: "/chat",
        component: ChatPage,
      })
    )
  }

  const router = createRouter({
    routeTree: rootRoute.addChildren(routes),
    history: createMemoryHistory({ initialEntries: [initial] }),
  })

  const tree = render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <SessionProvider user={user} projects={PROJECTS_SEED}>
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </QueryClientProvider>
      </SessionProvider>
    </ThemeProvider>
  )

  return { tree, router }
}

/** The tree, once the router has actually painted it. */
async function mounted(initial: string, user?: SessionUser, chatRoute?: boolean) {
  const tree = mountTree(initial, user, chatRoute)
  await waitFor(() => expect(at("dock-tree-mounted")).not.toBeNull())
  return tree
}

/** The seeded shift, a trigger press, and the sheet it opens. */
async function openSheet(initial = "/", user?: SessionUser) {
  const context = await mounted(initial, user)
  const trigger = await waitFor(() => {
    const found = at("chat-dock-trigger")
    expect(found).not.toBeNull()
    return found as HTMLElement
  })
  trigger.focus()
  fireEvent.click(trigger)

  await waitFor(() => expect(at("bottom-sheet")).not.toBeNull())
  await waitFor(() => expect(at("chat-console")).not.toBeNull())
  return context
}

describe("the floating trigger", () => {
  it("is there for a shift that may use the console, and says what it opens", async () => {
    await openSheet("/")
    const trigger = at("chat-dock-trigger") as HTMLElement
    // The label spells the chord the way this keyboard says it — jsdom's
    // user agent is not an Apple one, so the test hears the ctrl spelling.
    expect(trigger.getAttribute("aria-label")).toBe(
      "Open the console — ctrl j"
    )
    expect(trigger.getAttribute("aria-expanded")).toBe("true")
  })

  it("is not drawn for a role that cannot use the console", async () => {
    // The rail's rule, kept by the other door: navigation a role cannot use
    // is hidden, not explained.
    const context = await mounted("/", {
      ...SESSION_USER_SEED,
      platformRoles: ["viewer"],
      projectRoles: {},
    })
    expect(at("chat-dock-trigger")).toBeNull()
    // And the chord is not armed either — a shortcut that opened a door the
    // rail hides would be a door.
    fireEvent.keyDown(document, { key: "j", ctrlKey: true })
    expect(at("bottom-sheet")).toBeNull()
    context.tree.unmount()
  })

  it("answers its chord from anywhere, both ways", async () => {
    // The chord is the point: the operator is three panels deep in a table,
    // presses ctrl j, and the console is there — not after finding the
    // trigger. The same chord closes, because "open" and "get out of my way"
    // are the two halves of one gesture.
    const context = await mounted("/")

    fireEvent.keyDown(document, { key: "j", ctrlKey: true })
    await waitFor(() => expect(at("bottom-sheet")).not.toBeNull())

    fireEvent.keyDown(document, { key: "j", ctrlKey: true })
    await waitFor(() => expect(at("bottom-sheet")).toBeNull())
    context.tree.unmount()
  })

  it("leaves the chord alone without its modifier", async () => {
    const context = await mounted("/")
    // A bare j is a letter somebody is typing into a filter.
    fireEvent.keyDown(document, { key: "j" })
    expect(at("bottom-sheet")).toBeNull()
    context.tree.unmount()
  })

  it("puts the caret in the composer the moment the sheet opens", async () => {
    const mounted = await openSheet("/")
    await waitFor(() =>
      expect(document.activeElement?.getAttribute("data-test")).toBe(
        "chat-input"
      )
    )
    mounted.tree.unmount()
  })

  it("closes on a click in the empty window above the sheet", async () => {
    // The owner's gesture: the dark is the cheapest way out. Safe here and
    // only here — the conversation, the draft and the depth all live outside
    // the sheet's tree, so a stray click costs nothing.
    const mounted = await openSheet("/")

    const box = document.querySelector<HTMLTextAreaElement>(
      '[data-test="chat-input"]'
    )
    fireEvent.change(box as HTMLTextAreaElement, {
      target: { value: "почему очередь стоит" },
    })

    fireEvent.click(at("bottom-sheet-scrim-hit") as HTMLElement)
    await waitFor(() => expect(at("bottom-sheet")).toBeNull())

    // And what it cost: nothing. The draft is exactly where it was.
    fireEvent.click(at("chat-dock-trigger") as HTMLElement)
    const again = await waitFor(() => {
      const found = document.querySelector<HTMLTextAreaElement>(
        '[data-test="chat-input"]'
      )
      expect(found).not.toBeNull()
      return found as HTMLTextAreaElement
    })
    expect(again.value).toBe("почему очередь стоит")
    mounted.tree.unmount()
  })
})

describe("the sheet", () => {
  it("closes on escape and returns focus to the trigger", async () => {
    const mounted = await openSheet()
    expect(document.activeElement).not.toBe(at("chat-dock-trigger"))

    fireEvent.keyDown(at("bottom-sheet") as HTMLElement, { key: "Escape" })

    await waitFor(() => expect(at("bottom-sheet")).toBeNull())
    // A closed door leaves you where you came in — restored by an effect as
    // the overlay unmounts, so it is waited for, not assumed synchronous.
    await waitFor(() =>
      expect(document.activeElement).toBe(at("chat-dock-trigger"))
    )
    mounted.tree.unmount()
  })

  it("renders the same console the /chat route renders", async () => {
    // Not by eye and not by faith: same component, same hashed class, same
    // test id on the composer. The day someone forks the console, this
    // comparison is what catches it.
    const route = mountTree("/chat", SESSION_USER_SEED, true)
    const onRoute = await waitFor(() => {
      const console = at("chat-console")
      expect(console).not.toBeNull()
      expect(console?.contains(at("chat-composer") as Node)).toBe(true)
      return console as HTMLElement
    })
    const routeClass = onRoute.getAttribute("class")
    route.tree.unmount()

    const dock = await openSheet("/")
    const inSheet = at("chat-console") as HTMLElement
    expect(inSheet.getAttribute("class")).toBe(routeClass)
    expect(inSheet.contains(at("chat-composer") as Node)).toBe(true)
    dock.tree.unmount()
  })

  it("fills the window from a control, and comes back out of it", async () => {
    const mounted = await openSheet()
    const sheet = at("bottom-sheet") as HTMLElement
    expect(sheet.getAttribute("data-expanded")).toBeNull()

    fireEvent.click(at("bottom-sheet-expand") as HTMLElement)
    await waitFor(() =>
      expect(
        at("bottom-sheet")?.getAttribute("data-expanded")
      ).not.toBeNull()
    )

    // Reversible, by the same control.
    fireEvent.click(at("bottom-sheet-expand") as HTMLElement)
    await waitFor(() =>
      expect(at("bottom-sheet")?.getAttribute("data-expanded")).toBeNull()
    )
    mounted.tree.unmount()
  })

  it("seeds the composer with what the screen was about", async () => {
    // Opened from a run's detail: the run comes with the operator, past the
    // scrim that hides it. One gesture drops it again — see the composer's
    // own tests; here the question is that the location reached the chip.
    const mounted = await openSheet("/runs/5b1d7e40")
    const chip = await waitFor(() => {
      const found = at("chat-seed")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(chip.textContent).toContain("5b1d7e40")
    mounted.tree.unmount()
  })

  it("seeds nothing from a screen that names no entity", async () => {
    const mounted = await openSheet("/queue")
    await waitFor(() => expect(at("chat-console")).not.toBeNull())
    expect(at("chat-seed")).toBeNull()
    mounted.tree.unmount()
  })

  it("carries the onboarding link the rail item used to own", async () => {
    // The console stopped being a section, so the wizard's entry point moved
    // into the sheet's bar — an entry point must arrive in the container that
    // replaced the one it lived in.
    const mounted = await openSheet("/")
    const link = await waitFor(() => {
      const found = at("chat-init")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(link.getAttribute("href")).toBe("/chat/init")

    // And pressing it hands the wizard the whole window rather than opening
    // it under a scrim.
    fireEvent.click(link, { button: 0 })
    await waitFor(() => expect(at("bottom-sheet")).toBeNull())
    mounted.tree.unmount()
  })

  it("offers onboarding only where the session may connect a source", async () => {
    const mounted = await openSheet("/", {
      ...SESSION_USER_SEED,
      platformRoles: ["member"],
      projectRoles: {},
    })
    expect(at("chat-init")).toBeNull()
    mounted.tree.unmount()
  })

  it("keeps a draft across close and reopen", async () => {
    const mounted = await openSheet("/")
    const box = await waitFor(() => {
      const found = document.querySelector<HTMLTextAreaElement>(
        '[data-test="chat-input"]'
      )
      expect(found).not.toBeNull()
      return found as HTMLTextAreaElement
    })

    fireEvent.change(box, { target: { value: "почему очередь стоит" } })
    fireEvent.keyDown(at("bottom-sheet") as HTMLElement, { key: "Escape" })
    await waitFor(() => expect(at("bottom-sheet")).toBeNull())

    fireEvent.click(at("chat-dock-trigger") as HTMLElement)
    const again = await waitFor(() => {
      const found = document.querySelector<HTMLTextAreaElement>(
        '[data-test="chat-input"]'
      )
      expect(found).not.toBeNull()
      return found as HTMLTextAreaElement
    })
    expect(again.value).toBe("почему очередь стоит")
    mounted.tree.unmount()
  })

  it("gets out of the way when a hand-off is pressed", async () => {
    // A hand-off's whole answer is a screen, narrowed. Navigating under a
    // scrim the operator cannot see through would be the sheet lying about
    // where it is — so the link takes the sheet with it.
    const mounted = await openSheet("/")
    const rows = await waitFor(() => {
      const found = all("chat-session")
      expect(found.length).toBeGreaterThan(0)
      return found
    })
    const fresh = rows.find((row) =>
      row.textContent?.startsWith("New conversation")
    )
    fireEvent.click(fresh as HTMLElement)

    const box = await waitFor(() => {
      const found = document.querySelector<HTMLTextAreaElement>(
        '[data-test="chat-input"]'
      )
      expect(found).not.toBeNull()
      return found as HTMLTextAreaElement
    })
    fireEvent.change(box, { target: { value: "/status" } })
    fireEvent.keyDown(box, { key: "Enter" })

    const handoff = await waitFor(() => {
      const found = at("chat-handoff")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    fireEvent.click(handoff, { button: 0 })

    await waitFor(() => expect(at("bottom-sheet")).toBeNull())
    await waitFor(() =>
      expect(
        `${mounted.router.state.location.pathname}${mounted.router.state.location.searchStr}`
      ).toBe("/runs?q=waiting")
    )
    mounted.tree.unmount()
  })
})
