import { createContext, useContext, useMemo, type ReactNode } from "react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import type {
  SourceConnection,
  SourceKind,
  SourceState,
} from "@/domains/sources/model/types"
import {
  createSourceColumns,
  getConnectionId,
} from "@/domains/sources/ui/sources-columns"
import { useSession, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { DataTable } from "@/shared/ui"

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
    value: 1200,
  })
})

/* The source cell is a `<Link>` to that source's own page now, so the list
   only renders inside a router. A memory router carrying the section's four
   paths gives the cells working destinations without dragging in the generated
   route tree. */
const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  [
    "/",
    "/sources",
    "/sources/new",
    "/sources/$sourceId",
    "/sources/$sourceId/ticket/new",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

/* `RouterProvider` loads its first match before it renders anything, so every
   mount here is awaited. That is the whole cost of the source cell becoming a
   real link, and it is worth it: the destination is what makes a connection
   something an operator can paste into a ticket. */
async function renderInRouter(node: ReactNode, firstRow: string) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/sources"] }),
  })
  render(
    <SlotContext value={node}>
      <RouterProvider router={router} />
    </SlotContext>
  )
  await screen.findByText(firstRow)
}

function connection(
  overrides: Partial<SourceConnection> & {
    id: string
    projectId: string
    kind: SourceKind
    name: string
    state: SourceState
  }
): SourceConnection {
  return {
    auth: "pat",
    selfHosted: false,
    account: "svc-bot",
    removable: true,
    lastSyncAt: "3 min ago",
    watch: {
      enabled: true,
      filter: "labels: swarm",
      mode: "inbox-only",
      matched: 4,
      mapping: [{ from: "success", to: "close the issue" }],
    },
    ...overrides,
  }
}

const HERE = connection({
  id: "src_here",
  projectId: "p_test",
  kind: "github",
  name: "here/web-app",
  state: "connected",
})

const THERE = connection({
  id: "src_there",
  projectId: "p_other",
  kind: "jira",
  name: "there",
  state: "error",
  reason:
    "401 from other.atlassian.net — the api token was revoked on 24 aug. reconnect with a new one.",
  baseUrl: "https://other.atlassian.net",
})

const NATIVE = connection({
  id: "src_native",
  projectId: "p_test",
  kind: "native",
  name: "native intake",
  state: "connected",
  auth: "none",
  removable: false,
  watch: null,
})

const CONNECTIONS = [HERE, THERE, NATIVE]

/**
 * The connections list exactly as `SourcesPage` assembles it: the session is
 * read by a component and travels into the column factory as a value. That is
 * the arrangement under test as much as the buttons are — a `cell` runs as a
 * plain function while TanStack builds a row, so a `useCan` moved down into one
 * would throw at the first render, and a `useCan` left up here would answer for
 * the whole screen when the question belongs to the row.
 */
function List({
  onOpenSource,
  onTest,
  onDisconnect,
  onNewTicket,
}: {
  onOpenSource: (connection: SourceConnection) => void
  onTest: (connection: SourceConnection) => void
  onDisconnect: (connection: SourceConnection) => void
  onNewTicket: (connection: SourceConnection) => void
}) {
  const session = useSession()

  const columns = useMemo(
    () =>
      createSourceColumns({
        projects: session.projects,
        tickets: [],
        testingId: null,
        onOpenSource,
        onTest,
        onDisconnect,
        onNewTicket,
        session,
      }),
    [session, onOpenSource, onTest, onDisconnect, onNewTicket]
  )

  return (
    <DataTable
      columns={columns}
      data={CONNECTIONS}
      getRowId={getConnectionId}
      density="compact"
    />
  )
}

async function mount(
  roles: Role[],
  projectRoles: Record<string, Role[]> = {}
) {
  const onOpenSource = vi.fn()
  const onTest = vi.fn()
  const onDisconnect = vi.fn()
  const onNewTicket = vi.fn()

  await renderInRouter(
    <TestSession roles={roles} projectRoles={projectRoles}>
      <List
        onOpenSource={onOpenSource}
        onTest={onTest}
        onDisconnect={onDisconnect}
        onNewTicket={onNewTicket}
      />
    </TestSession>,
    HERE.name
  )

  const watch = (entry: SourceConnection) =>
    screen.getByRole("button", { name: `Edit the watch on ${entry.name}` })
  const test = (entry: SourceConnection) =>
    screen.getByRole("button", { name: `Test the connection to ${entry.name}` })
  const disconnect = (entry: SourceConnection) =>
    screen.getByRole("button", { name: `Disconnect ${entry.name}` })

  return {
    onOpenSource,
    onTest,
    onDisconnect,
    onNewTicket,
    watch,
    test,
    disconnect,
  }
}

describe("a connection that is broken says why", () => {
  it("puts the provider's own sentence on the row", async () => {
    await mount(["operator"], { p_test: ["project-admin"] })

    // The badge says *that* it is broken; this cell is the only place on the
    // screen that says *why*, so a code with no sentence would send the
    // operator to the provider to find out what a line here could have told
    // them.
    expect(
      screen.getByText(
        "401 from other.atlassian.net — the api token was revoked on 24 aug. reconnect with a new one."
      )
    ).toBeTruthy()
  })

  it("marks the state in a word as well as a hue", async () => {
    await mount(["operator"], { p_test: ["project-admin"] })

    // `data-test`, not `data-testid` — this project's own hook for a control.
    const states = [
      ...document.querySelectorAll('[data-test="connection-state"]'),
    ].map((node) => node.getAttribute("data-state"))

    expect(states).toContain("error")
    expect(states).toContain("connected")
    // The word is in the badge's own text, not carried by colour alone.
    expect(screen.getAllByText("error").length).toBeGreaterThan(0)
  })

  it("says when a healthy watch is admitting nothing", async () => {
    const idle = connection({
      id: "src_idle",
      projectId: "p_test",
      kind: "gitlab",
      name: "here/identity-svc",
      state: "connected",
      watch: {
        enabled: true,
        filter: "labels: agent-ready",
        mode: "watch",
        matched: 0,
        mapping: [],
      },
    })

    function Idle() {
      const session = useSession()
      const columns = createSourceColumns({
        projects: session.projects,
        tickets: [],
        testingId: null,
        onOpenSource: () => {},
        onTest: () => {},
        onDisconnect: () => {},
        onNewTicket: () => {},
        session,
      })
      return (
        <DataTable
          columns={columns}
          data={[idle]}
          getRowId={getConnectionId}
          density="compact"
        />
      )
    }

    await renderInRouter(
      <TestSession roles={["platform-admin"]}>
        <Idle />
      </TestSession>,
      idle.name
    )

    // Nothing is broken, and that is exactly why it needs saying: a working
    // connection with a filter nobody has updated looks identical to a busy one.
    expect(
      screen.getByText("the filter matched nothing in the last day")
    ).toBeTruthy()
  })
})

describe("native intake refuses to be disconnected", () => {
  it("keeps the control and explains the refusal", async () => {
    const { disconnect } = await mount(["platform-admin"])
    const button = disconnect(NATIVE)

    // Present, in the same cell, at the same size — hiding it would leave the
    // operator wondering whether they simply lack the role.
    expect(document.body.contains(button)).toBe(true)
    expect(button.getAttribute("aria-disabled")).toBe("true")
    expect(button.getAttribute("data-denied")).toBe(
      "native intake cannot be disconnected — it is the product's own way of accepting a ticket"
    )

    // `disabled` would drop it out of the tab order and kill the hover that
    // carries the sentence — the explanation would exist and be unreachable.
    expect(button.hasAttribute("disabled")).toBe(false)
  })

  it("swallows the click even for a platform admin", async () => {
    const { disconnect, onDisconnect } = await mount(["platform-admin"])

    fireEvent.click(disconnect(NATIVE))

    expect(onDisconnect).not.toHaveBeenCalled()
  })

  it("offers a ticket instead of a watch, because native has no watch", async () => {
    await mount(["platform-admin"])

    expect(
      screen.getByRole("button", {
        name: "New ticket in native intake on test",
      })
    ).toBeTruthy()
    expect(
      screen.queryByRole("button", {
        name: "Edit the watch on native intake",
      })
    ).toBeNull()
    // Twice: once as the source's name, once as its admission — "watch off"
    // would be a lie about a connection that has no watch to turn off.
    expect(screen.getAllByText("native intake")).toHaveLength(2)
  })
})

describe("one list, two projects, two different answers", () => {
  it("puts a live edit directly above an explained one", async () => {
    // The whole point of the screen: project-admin here, viewer there, and both
    // rows on the board at once. A single answer for the session would be wrong
    // on one of these two rows whichever way it went.
    const { watch } = await mount(["viewer"], { p_test: ["project-admin"] })

    expect(watch(HERE).hasAttribute("aria-disabled")).toBe(false)
    // Granted, so there is nothing to explain: `title` is the slot `denied`
    // writes its sentence into, and an available act writes nothing there. The
    // word the pencil stands in for now arrives through the kit tooltip — on
    // focus as well as on hover, which the attribute never managed — and the
    // name the control is reachable by has not moved.
    expect(watch(HERE).getAttribute("data-denied")).toBeNull()
    expect(watch(HERE).getAttribute("aria-label")).toBe(
      "Edit the watch on here/web-app"
    )

    expect(watch(THERE).getAttribute("aria-disabled")).toBe("true")
    // Naming the project is what stops the second row reading as a flat no to
    // someone who has been administering the first one all shift.
    expect(watch(THERE).getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin on other"
    )
    expect(watch(THERE).hasAttribute("disabled")).toBe(false)
  })

  it("decides on the row that was clicked and on no other", async () => {
    const { watch, onOpenSource } = await mount(["viewer"], {
      p_test: ["project-admin"],
    })

    fireEvent.click(watch(THERE))
    expect(onOpenSource).not.toHaveBeenCalled()

    fireEvent.click(watch(HERE))
    expect(onOpenSource).toHaveBeenCalledTimes(1)
    expect(onOpenSource).toHaveBeenCalledWith(HERE)
  })

  it("gates testing and disconnecting per row too, not just the watch", async () => {
    const { test, disconnect, onTest, onDisconnect } = await mount(["viewer"], {
      p_test: ["project-admin"],
    })

    expect(test(HERE).hasAttribute("aria-disabled")).toBe(false)
    expect(test(THERE).getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin on other"
    )
    expect(disconnect(THERE).getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin on other"
    )

    fireEvent.click(test(THERE))
    fireEvent.click(disconnect(THERE))
    expect(onTest).not.toHaveBeenCalled()
    expect(onDisconnect).not.toHaveBeenCalled()

    fireEvent.click(test(HERE))
    expect(onTest).toHaveBeenCalledWith(HERE)
  })

  it("refuses every act to a viewer, and says what each one needs", async () => {
    const { watch, test, disconnect, onOpenSource } = await mount(["viewer"])

    for (const button of [watch(HERE), test(HERE), disconnect(HERE)]) {
      expect(document.body.contains(button)).toBe(true)
      expect(button.getAttribute("aria-disabled")).toBe("true")
      expect(button.getAttribute("data-denied")).toBe(
        "needs project-admin or platform-admin on test"
      )
      expect(button.hasAttribute("disabled")).toBe(false)
    }

    fireEvent.click(watch(HERE))
    expect(onOpenSource).not.toHaveBeenCalled()
  })

  it("carries a platform role into every project at once", async () => {
    // The other half of the rule: a platform grant is not scoped, so it opens
    // both rows. A row-level answer is not a per-project ceiling.
    const { watch } = await mount(["platform-admin"])

    expect(watch(HERE).hasAttribute("aria-disabled")).toBe(false)
    expect(watch(THERE).hasAttribute("aria-disabled")).toBe(false)
  })

  it("gates a native ticket on taking work, not on administering sources", async () => {
    // Writing a bug down is a member's act. Requiring a project administrator
    // for it would be the wrong shape even on an administrator's screen.
    await mount(["viewer"], { p_test: ["member"] })

    const take = screen.getByRole("button", {
      name: "New ticket in native intake on test",
    })
    expect(take.hasAttribute("aria-disabled")).toBe(false)

    // …and the same person still cannot disconnect anything.
    expect(
      screen
        .getByRole("button", { name: "Disconnect here/web-app" })
        .getAttribute("data-denied")
    ).toBe("needs project-admin or platform-admin on test")
  })

  it("shows the project each row belongs to, by its key", async () => {
    await mount(["platform-admin"])

    // Not the id and not the display name: the key is what the operator calls
    // it, and it is the word the denial sentence uses.
    expect(screen.getAllByText("test").length).toBeGreaterThan(0)
    expect(screen.getByText("other")).toBeTruthy()
  })
})

/* Where the pencil used to open a modal it now travels, and the identifier
   cell is what carries the address. The two are deliberately different
   elements: a gated act has to be able to refuse and explain itself, which is a
   button's job, and a destination has to be openable in a tab and copyable,
   which is an anchor's. */
describe("a row is a way into its source's own page", () => {
  it("puts the address on the identifier cell and nowhere else", async () => {
    await mount(["platform-admin"])

    const links = [
      ...document.querySelectorAll('[data-test="source-link"]'),
    ] as HTMLAnchorElement[]

    expect(links.map((link) => link.textContent)).toEqual([
      "here/web-app",
      "there",
      "native intake",
    ])
    expect(links[0].getAttribute("href")).toBe("/sources/src_here")

    // Not the row: a row-wide click target would swallow the buttons in the
    // actions column, which is four acts lost to one gesture.
    const row = links[0].closest("[data-test='data-table-row']")
    expect(row?.getAttribute("href")).toBeFalsy()
    expect(row?.tagName.toLowerCase()).not.toBe("a")
  })

  it("keeps the gated act a button that refuses, not an anchor", async () => {
    const { watch, onOpenSource } = await mount(["viewer"], {
      p_test: ["project-admin"],
    })

    // `denied` is a button's property. An anchor has no way to refuse a click
    // and say what would open it, which is why this one did not become a link
    // when its destination did.
    expect(watch(THERE).tagName.toLowerCase()).toBe("button")
    expect(watch(THERE).getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin on other"
    )

    fireEvent.click(watch(HERE))
    expect(onOpenSource).toHaveBeenCalledWith(HERE)
  })
})
