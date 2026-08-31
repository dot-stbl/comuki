import { createContext, useContext, useMemo, type ReactNode } from "react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeAll, describe, expect, it, vi } from "vitest"

import {
  SOURCE_KINDS,
  SOURCE_KIND_BRAND,
} from "@/domains/sources/model/providers"
import type {
  SourceConnection,
  SourceKind,
} from "@/domains/sources/model/types"
import {
  createSourceColumns,
  getConnectionId,
} from "@/domains/sources/ui/sources-columns"
import { useSession } from "@/shared/session"
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
    value: 480,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1200,
  })
})

/* The source cell is a `<Link>` to that source's own page now, so the list only
   renders inside a router. */
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
   mount here is awaited. */
async function renderInRouter(node: ReactNode) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/sources"] }),
  })
  render(
    <SlotContext value={node}>
      <RouterProvider router={router} />
    </SlotContext>
  )
  await screen.findByText("github source")
}

/** One row per provider, so every branch of the mark rule is on the board. */
const CONNECTIONS: SourceConnection[] = SOURCE_KINDS.map(
  (kind: SourceKind) => ({
    id: `src_${kind}`,
    projectId: "p_test",
    kind,
    name: `${kind} source`,
    state: "connected",
    auth: kind === "native" ? "none" : "pat",
    selfHosted: false,
    account: "svc-bot",
    removable: kind !== "native",
    lastSyncAt: "3 min ago",
    watch:
      kind === "native"
        ? null
        : {
            enabled: true,
            filter: "labels: swarm",
            mode: "inbox-only",
            matched: 4,
            mapping: [],
          },
  })
)

function List() {
  const session = useSession()
  const columns = useMemo(
    () =>
      createSourceColumns({
        projects: session.projects,
        tickets: [],
        testingId: null,
        onOpenSource: vi.fn(),
        onTest: vi.fn(),
        onDisconnect: vi.fn(),
        onNewTicket: vi.fn(),
        session,
      }),
    [session]
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

async function mount() {
  await renderInRouter(
    <TestSession roles={["platform-admin"]}>
      <List />
    </TestSession>
  )
}

const tags = () =>
  Array.from(document.querySelectorAll('[data-test="brand-tag"]'))

describe("a provider is shown as its mark", () => {
  it("draws one for every provider that has one", async () => {
    await mount()

    const drawn = tags()
      .map((tag) => tag.getAttribute("data-brand"))
      .filter((brand) => brand !== "none")

    // Three vendors and the product's own intake. The count is the assertion:
    // a provider that quietly lost its mark would still render a cell.
    expect(drawn).toEqual(["github", "gitlab", "jira", "comuki"])
  })

  it("keeps the provider's name on every mark it draws", async () => {
    await mount()

    // A monochrome glyph at table size is a recognition cue and nothing more.
    // Whoever is not looking at it — or is looking and does not recognise it —
    // gets the same word the column used to spell.
    for (const kind of SOURCE_KINDS) {
      if (!SOURCE_KIND_BRAND[kind]) continue
      expect(
        screen.getByRole("img", { name: kind === "native" ? "native" : kind })
      ).not.toBeNull()
    }
  })

  it("spells the one provider whose mark would be a guess", async () => {
    await mount()

    // Yandex publishes no monochrome Tracker mark and the product glyph is
    // carried by its colour. Drawing one from memory would be inventing a
    // trademark; the column says the words instead, and says them in full.
    const spelled = tags().filter(
      (tag) => tag.getAttribute("data-brand") === "none"
    )
    expect(spelled).toHaveLength(1)
    expect(spelled[0].textContent).toBe("yandex tracker")
    expect(screen.queryByRole("img", { name: /yandex/i })).toBeNull()
  })

  it("hands the word back on hover, for the reader who does not know it", async () => {
    await mount()

    // Not the kit `Tooltip`: that one is built on React Aria's `Focusable`,
    // which makes its trigger a tab stop. A tab stop per row to describe
    // something nobody can act on is worse than the attribute every other cell
    // in this table already uses.
    const octocat = tags().find(
      (tag) => tag.getAttribute("data-brand") === "github"
    )
    expect(octocat?.getAttribute("title")).toBe("github")
  })
})

/**
 * Tab until the control has focus.
 *
 * Focus rather than hover, and a real tab rather than `element.focus()`: React
 * Aria opens on focus with no dwell but only once it believes a keyboard is
 * driving, and a bare `.focus()` call never tells it that. It is also the
 * reading under test — a tooltip only a mouse can reach is a tooltip half the
 * operators on this screen do not have.
 */
async function tabTo(
  user: ReturnType<typeof userEvent.setup>,
  target: HTMLElement
) {
  for (let step = 0; step < 40; step += 1) {
    if (document.activeElement === target) return
    await user.tab()
  }
  throw new Error("the control never took focus")
}

describe("an icon-only act says what it does", () => {
  it("gives the kit tooltip the word, on focus and not only on hover", async () => {
    const user = userEvent.setup()
    await mount()

    const button = screen.getByRole("button", {
      name: "Test the connection to github source",
    })

    expect(screen.queryByRole("tooltip")).toBeNull()

    await tabTo(user, button)

    // The tooltip describes; it never becomes the name. A control whose only
    // name is its tooltip has no name at all while the pointer is elsewhere.
    const tip = await screen.findByRole("tooltip")
    expect(tip.textContent).toBe("Test connection")
    expect(button.getAttribute("aria-describedby")).not.toBeNull()
    expect(button.getAttribute("aria-label")).toBe(
      "Test the connection to github source"
    )
  })

  it("gives a refused act its refusal in the same place", async () => {
    const user = userEvent.setup()
    await renderInRouter(
      <TestSession roles={["viewer"]}>
        <List />
      </TestSession>
    )

    const button = screen.getByRole("button", {
      name: "Disconnect github source",
    })

    // `denied`, not `disabled`: an `aria-disabled` control keeps its focus and
    // its hover, which is the only way the sentence is reachable at all.
    expect(button.getAttribute("aria-disabled")).toBe("true")
    expect(button.hasAttribute("disabled")).toBe(false)

    await tabTo(user, button)

    expect((await screen.findByRole("tooltip")).textContent).toBe(
      "needs project-admin or platform-admin on test"
    )
  })
})
