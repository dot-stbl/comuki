import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { ChatPage } from "@/domains/chat/pages/chat-page"
import {
  listChatSessions,
  resetChatSessions,
} from "@/shared/api/mock/chat.store"
import { listSeedRuns, resetSeedRuns } from "@/shared/api/mock/runs.store"
import { PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider, type SessionUser } from "@/shared/session"

/* The screen serves the mock, and whether it does is normally an environment
   variable that is not committed. Pinned here so this is a test of the screen. */
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

  /* `SplitPane` persists the shell rail's divider position, and in jsdom the
     layout it saves was measured against nothing. Restoring that on the next
     mount makes `react-resizable-panels` throw `No layout data found for index
     0`, and every case here mounts the shell again in the same document. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  resetChatSessions()
  resetSeedRuns()
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
 * The seeded shift, not a synthetic one.
 *
 * The chat seed is written against `p_comuki` / `p_plexor` / `p_atlas` and the
 * roles this shift holds on each, and the whole demonstration is that the same
 * act gets three different answers. A generic two-project test session would
 * refuse everything for the wrong reason — no grants at all rather than the
 * grants a real shift has — and would prove nothing about the rule.
 */
function mount(user: SessionUser = SESSION_USER_SEED) {
  const rootRoute = createRootRoute()
  const blank = () => null

  const chat = createRoute({
    getParentRoute: () => rootRoute,
    path: "/chat",
    component: ChatPage,
  })

  const routeTree = rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    chat,
  ])

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/chat"] }),
  })

  render(
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

  return router
}

/** The seeded conversation whose title starts with `title`. */
async function openSession(title: string) {
  const rows = await waitFor(() => {
    const found = all("chat-session")
    expect(found.length).toBeGreaterThan(0)
    return found
  })
  const row = rows.find((entry) => entry.textContent?.startsWith(title))
  expect(row).toBeDefined()
  fireEvent.click(row as HTMLElement)
}

describe("the console, on the seeded shift", () => {
  it("opens on the newest conversation and lists the rest", async () => {
    mount()
    await waitFor(() => expect(all("chat-session").length).toBe(5))
    expect(at("chat-thread")).not.toBeNull()
  })

  it("renders the reply that is mid-flight outside the log", async () => {
    mount()
    await openSession("Разбор падения")

    await waitFor(() => expect(at("chat-streaming")).not.toBeNull())
    expect(at("chat-log")?.contains(at("chat-streaming") as Node)).toBe(false)
  })
})

describe("a confirmed proposal", () => {
  it("is not applied until it is pressed", async () => {
    mount()
    await waitFor(() => expect(at("chat-proposal")).not.toBeNull())

    // Mounting the console is not a decision. The seeded plan is still open,
    // and the run it would release is still waiting on a human.
    expect(at("chat-proposal-decided")).toBeNull()
    expect(listSeedRuns().find((run) => run.id === "5b1d7e40")?.status).toBe(
      "waiting"
    )
  })

  it("writes to the store, and survives a refetch", async () => {
    const router = mount()
    await waitFor(() => expect(at("chat-proposal-confirm")).not.toBeNull())

    fireEvent.click(at("chat-proposal-confirm") as HTMLElement)

    await waitFor(() =>
      expect(at("chat-proposal-decided")).not.toBeNull()
    )

    // The store, not the render: a query whose `queryFn` returns a module
    // constant would revert this about 200ms after the optimistic write.
    const held = listChatSessions()
      .flatMap((session) => session.messages)
      .find((message) => message.proposal?.id === "cp_plan_5b1d")
    expect(held?.proposal?.decision).toBe("confirmed")

    // And it landed where every other decision lands. A run released from the
    // console is a run released — the duty screens read the same store.
    expect(listSeedRuns().find((run) => run.id === "5b1d7e40")?.status).toBe(
      "running"
    )

    // Leaving the screen and coming back reads the store again, and the
    // decision is still there.
    await router.navigate({ to: "/runs" })
    await router.navigate({ to: "/chat" })
    await waitFor(() => expect(at("chat-proposal-decided")).not.toBeNull())
  })
})

describe("a proposal this shift may not confirm", () => {
  it("keeps the control, refuses it, and changes nothing when pressed", async () => {
    mount()
    await openSession("Ротация ключей")

    const confirm = await waitFor(() => {
      const found = at("chat-proposal-confirm")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })

    // `operator` carries `runs.stop` everywhere, so the refusal is seeded on
    // the act it genuinely does not carry: approving a plan on `p_plexor`,
    // where this shift is a viewer.
    expect(confirm.getAttribute("aria-disabled")).toBe("true")
    expect(confirm.getAttribute("data-denied")).toBe(
      "needs approver, project-admin or platform-admin on plexor"
    )
    expect(confirm.hasAttribute("disabled")).toBe(false)

    fireEvent.click(confirm)
    expect(at("chat-proposal-decided")).toBeNull()

    const held = listChatSessions()
      .flatMap((session) => session.messages)
      .find((message) => message.proposal?.id === "cp_plan_2a6f")
    expect(held?.proposal?.decision).toBeUndefined()
  })
})

describe("the console answers, and hands off rather than drawing a list", () => {
  it("replies from the script and offers a filter on the real screen", async () => {
    mount()
    await openSession("New conversation")

    const box = await screen.findByLabelText("Message the console")
    fireEvent.change(box, { target: { value: "/status" } })
    fireEvent.keyDown(box, { key: "Enter" })

    await waitFor(() => expect(at("chat-handoff")).not.toBeNull())
    expect(at("chat-handoff")?.getAttribute("href")).toBe("/runs?q=waiting")
    // And a tool call is shown as a record, not summarised away.
    expect(at("chat-tool")).not.toBeNull()
  })

  it("actually lands on the narrowed screen when the hand-off is pressed", async () => {
    // The href alone is not the contract — a link that renders the right
    // address and then navigates to an unfiltered list is the exact failure a
    // hand-off exists to avoid.
    const router = mount()
    await openSession("New conversation")

    const box = await screen.findByLabelText("Message the console")
    fireEvent.change(box, { target: { value: "/status" } })
    fireEvent.keyDown(box, { key: "Enter" })

    const handoff = await waitFor(() => {
      const found = at("chat-handoff")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })

    fireEvent.click(handoff, { button: 0 })
    await waitFor(() =>
      expect(
        `${router.state.location.pathname}${router.state.location.searchStr}`
      ).toBe("/runs?q=waiting")
    )
  })

  it("turns an identifier in a reply into the thing it names", async () => {
    mount()
    await waitFor(() => expect(all("chat-reference").length).toBeGreaterThan(0))

    const run = all("chat-reference").find(
      (node) => node.textContent === "5b1d7e40"
    )
    expect(run?.getAttribute("href")).toBe("/runs/5b1d7e40")
  })

  it("opens the run a reference names", async () => {
    const router = mount()
    const run = await waitFor(() => {
      const found = all("chat-reference").find(
        (node) => node.textContent === "5b1d7e40"
      )
      expect(found).toBeDefined()
      return found as HTMLElement
    })

    fireEvent.click(run, { button: 0 })
    await waitFor(() =>
      expect(router.state.location.pathname).toBe("/runs/5b1d7e40")
    )
  })
})

describe("onboarding is a screen, not a modal", () => {
  it("links to the wizard for a shift that may connect a source", async () => {
    // The seeded shift administers `p_atlas`, so `sources.edit` holds
    // somewhere — which is the rail's question and the right one here.
    mount()
    const link = await waitFor(() => {
      const found = at("chat-init")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(link.getAttribute("href")).toBe("/chat/init")
  })

  it("does not offer it to a shift that may not connect one anywhere", async () => {
    mount({
      ...SESSION_USER_SEED,
      platformRoles: ["viewer"],
      projectRoles: { p_comuki: ["approver"] },
    })
    await waitFor(() => expect(at("chat-thread")).not.toBeNull())
    expect(at("chat-init")).toBeNull()
  })
})
