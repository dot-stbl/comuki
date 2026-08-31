import { createContext, useContext } from "react"
import type { ReactNode } from "react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
  type AnyRouter,
} from "@tanstack/react-router"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import { Route as QueueRoute } from "@/routes/queue"
import { Route as RunsRoute } from "@/routes/runs/index"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

import { GlobalSearch } from "./global-search"

/* The policy half, driven the way a person drives it: a chord, a paste, and
   enter. What is asserted is where the router ended up — the palette's whole
   job is turning a string into an address, and the address is the contract the
   backend will inherit when `GET /resolve?q=` exists. */

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
    "/projects/new",
    "/identity",
    "/identity/users/new",
    "/identity/keys/new",
    "/identity/grants/new",
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

function renderSearch(roles?: Role[]) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  render(
    <TestSession roles={roles}>
      <SlotContext value={<GlobalSearch />}>
        <RouterProvider router={router} />
      </SlotContext>
    </TestSession>
  )

  return router as AnyRouter
}

const palette = () => document.querySelector('[data-test="command-palette"]')
const input = () =>
  document.querySelector<HTMLInputElement>(
    '[data-test="command-palette-input"]'
  )
const rows = () =>
  Array.from(
    document.querySelectorAll<HTMLElement>('[data-test="command-palette-item"]')
  )

/** Opens the palette with the chord and types the query into it. */
async function ask(
  user: ReturnType<typeof userEvent.setup>,
  query: string
): Promise<void> {
  await user.keyboard("{Control>}k{/Control}")
  await waitFor(() => expect(input()).not.toBeNull())
  await user.paste(query)
  await waitFor(() => expect(rows().length).toBeGreaterThan(0))
}

describe("the global search control", () => {
  it("opens from the bar's own control", async () => {
    const user = userEvent.setup()
    renderSearch(["platform-admin"])

    const trigger = await screen.findByRole("button", { name: /search/i })
    expect(palette()).toBeNull()

    await user.click(trigger)

    expect(palette()).not.toBeNull()
  })

  it("opens and closes on the chord, wherever the focus is", async () => {
    const user = userEvent.setup()
    renderSearch(["platform-admin"])

    await user.keyboard("{Control>}k{/Control}")
    expect(palette()).not.toBeNull()

    await user.keyboard("{Control>}k{/Control}")
    await waitFor(() => expect(palette()).toBeNull())
  })

  it("names the chord on the control it belongs to", async () => {
    renderSearch(["platform-admin"])

    // The whole interface of a shortcut is invisible; the control has to say
    // it, and it has to say the key this keyboard actually has.
    expect(
      await screen.findByRole("button", { name: "Search — ctrl k" })
    ).not.toBeNull()
  })
})

describe("what enter does with a resolved identifier", () => {
  it("opens the run a pasted run id names", async () => {
    const user = userEvent.setup()
    const router = renderSearch(["platform-admin"])

    await ask(user, "5b1d7e40")
    await user.keyboard("{ArrowDown}{Enter}")

    await waitFor(() =>
      expect(router.state.location.pathname).toBe("/runs/5b1d7e40")
    )
    // The palette closes behind the navigation and forgets the question.
    expect(palette()).toBeNull()
  })

  it("lands on the queue with the work item's own filter applied", async () => {
    const user = userEvent.setup()
    const router = renderSearch(["platform-admin"])

    await ask(user, "wi_0101")
    await user.keyboard("{ArrowDown}{Enter}")

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/queue")
      expect(router.state.location.search).toEqual({ q: "wi_0101" })
    })
  })

  it("lands on the pool with a worker's own filter applied", async () => {
    const user = userEvent.setup()
    const router = renderSearch(["platform-admin"])

    await ask(user, "wk_e34d")
    await user.keyboard("{ArrowDown}{Enter}")

    await waitFor(() =>
      expect(router.state.location.search).toEqual({ w: "wk_e34d" })
    )
  })
})

describe("what enter does with free text", () => {
  it("hands the query off to a screen, already narrowed", async () => {
    const user = userEvent.setup()
    const router = renderSearch(["platform-admin"])

    await ask(user, "webhook")

    // Nothing resolved and no section matched, so the only rows are offers to
    // narrow a screen — not a list of rows this client guessed at.
    expect(rows().map((row) => row.dataset.kind)).toEqual([
      "search",
      "search",
      "search",
    ])

    await user.keyboard("{ArrowDown}{Enter}")

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/runs")
      expect(router.state.location.search).toEqual({ q: "webhook" })
    })
  })

  it("goes to the section a word names", async () => {
    const user = userEvent.setup()
    const router = renderSearch(["platform-admin"])

    await ask(user, "queue")
    await user.keyboard("{ArrowDown}{Enter}")

    await waitFor(() => expect(router.state.location.pathname).toBe("/queue"))
  })
})

/* ------------------------------------------------------------------ *
 * The other end of the hand-off: the routes that receive it.
 *
 * The palette builds `/runs?q=webhook` and `/queue?w=wk_e34d`; these two are
 * what turn those strings back into a filter. Asserted against the real route
 * declarations rather than a copy, because the whole hand-off is a promise
 * about a parameter name and a copy is exactly how that promise gets broken.
 * ------------------------------------------------------------------ */

/**
 * The route's own validator, narrowed out of the router's union of forms.
 * TanStack allows a function, an adapter or a schema here; these two routes
 * write the plain function, and this is the one place that has to say so.
 */
function validatorOf(options: {
  validateSearch?: unknown
}): (search: Record<string, unknown>) => Record<string, string> {
  return options.validateSearch as (
    search: Record<string, unknown>
  ) => Record<string, string>
}

describe("the routes the hand-off lands on", () => {
  it("reads the runs list's `q` and drops an empty one", () => {
    const validate = validatorOf(RunsRoute.options)

    expect(validate({ q: "  webhook  " })).toEqual({ q: "webhook" })
    expect(validate({ q: "   " })).toEqual({})
    expect(validate({})).toEqual({})
  })

  it("reads the queue's two halves separately", () => {
    const validate = validatorOf(QueueRoute.options)

    expect(validate({ q: "wi_0101", w: "wk_e34d" })).toEqual({
      q: "wi_0101",
      w: "wk_e34d",
    })
    expect(validate({ w: "sha256:9c41ab" })).toEqual({ w: "sha256:9c41ab" })
  })
})
