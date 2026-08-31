import { createContext, useContext, type ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it } from "vitest"

import type { ApiKeyRow } from "@/domains/identity/model/types"
import { KeysPanel } from "@/domains/identity/ui/keys-panel"
import {
  listSeedApiKeys,
  resetSeedIdentity,
} from "@/shared/api/mock/identity.store"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* jsdom lays nothing out and the table body is virtualized, so without a port
   depth the rows — and with them the buttons under test — never render. */
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
    value: 960,
  })
})

const KEYS: ApiKeyRow[] = [
  {
    id: "k_ci",
    name: "ci-pipeline",
    prefix: "cmk_4e9c",
    status: "active",
    createdAt: "2026-06-02",
    lastUsedAt: "2026-08-30 09:41",
    expiresAt: null,
    expiresInDays: null,
    grants: ["member on comuki"],
  },
  {
    // Never used: the most common way a key leaks is by being forgotten.
    id: "k_mcp",
    name: "mcp-bridge",
    prefix: "cmk_1a77",
    status: "active",
    createdAt: "2026-08-11",
    lastUsedAt: null,
    expiresAt: null,
    expiresInDays: null,
    grants: ["member on atlas"],
  },
  {
    id: "k_legacy",
    name: "legacy-import",
    prefix: "cmk_77aa",
    status: "revoked",
    createdAt: "2026-01-09",
    lastUsedAt: "2026-05-30 14:52",
    expiresAt: null,
    expiresInDays: null,
    grants: [],
  },
]

/* Making a key is a form, so it is a page, so this panel's create act is a
   link — and a link only renders inside a router. The once-only secret moved
   with the form: see `pages/create-key-page.test.tsx`. */
const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/identity", "/identity/keys/new"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function Providers({
  roles,
  children,
}: {
  roles: Role[]
  children: ReactNode
}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/identity"] }),
  })
  return (
    <QueryClientProvider client={client}>
      <TestSession roles={roles}>
        <SlotContext value={children}>
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </SlotContext>
      </TestSession>
    </QueryClientProvider>
  )
}

/* The router resolves its first match asynchronously, so nothing is on screen
   until it has — hence the await on the toolbar's own row count. */
async function mount(roles: Role[] = ["platform-admin"]) {
  render(
    <Providers roles={roles}>
      <KeysPanel keys={KEYS} />
    </Providers>
  )
  await screen.findByText(/ shown$/)
}

beforeEach(() => {
  resetSeedIdentity()
})

describe("the list never holds a secret", () => {
  it("shows the prefix, which is all the store kept", async () => {
    await mount()

    expect(screen.getByText("cmk_4e9c")).toBeTruthy()
    // Nothing on this screen is or ever was the plaintext.
    expect(document.querySelector('[data-test="secret-value"]')).toBeNull()
  })

  it("sends creating a key to a page of its own", async () => {
    await mount()

    const create = screen.getByRole("link", { name: "New key" })
    expect(create.getAttribute("href")).toBe("/identity/keys/new")
  })
})

describe("revoking a key", () => {
  it("asks before it does it, and says what stops working", async () => {
    await mount()

    fireEvent.click(screen.getByRole("button", { name: "Revoke key cmk_4e9c" }))

    expect(screen.getByText("Revoke this key?")).toBeTruthy()
    // Nothing has happened yet — the question is the whole point.
    expect(listSeedApiKeys().find((key) => key.id === "k_ci")?.status).toBe(
      "active"
    )
  })

  it("leaves the key alone when the question is answered no", async () => {
    await mount()

    fireEvent.click(screen.getByRole("button", { name: "Revoke key cmk_4e9c" }))
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    expect(listSeedApiKeys().find((key) => key.id === "k_ci")?.status).toBe(
      "active"
    )
  })

  it("revokes on confirmation, and takes the grants with it", async () => {
    await mount()

    fireEvent.click(screen.getByRole("button", { name: "Revoke key cmk_4e9c" }))
    fireEvent.click(screen.getByRole("button", { name: "Revoke key" }))

    await waitFor(() =>
      expect(listSeedApiKeys().find((key) => key.id === "k_ci")?.status).toBe(
        "revoked"
      )
    )
  })

  it("offers no revoke on a key that is already gone", async () => {
    await mount()

    expect(
      screen.queryByRole("button", { name: "Revoke key cmk_77aa" })
    ).toBeNull()
  })

  /* A confirmation is not an edit, which is why this one stayed a dialog while
     every form on this section became a page: the decision is one sentence,
     and leaving the list to make it would lose the row being read. */
  it("keeps the question on the list rather than sending anyone to a screen", async () => {
    await mount()

    fireEvent.click(screen.getByRole("button", { name: "Revoke key cmk_4e9c" }))

    expect(
      document.querySelector('[data-test="confirm-dialog"]')
    ).not.toBeNull()
    // The rows are still behind it. Answering does not cost the operator
    // their place.
    expect(screen.getByText("cmk_1a77")).toBeTruthy()
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps both acts in the document and names what they need", async () => {
    await mount(["operator"])

    // Denied, the create act is a control that refuses and explains itself
    // rather than an anchor — an anchor has no way to say no. Both acts are
    // icon-only in a kit tooltip now, so the sentence arrives there and the
    // button drops its native title rather than saying it twice. The
    // `data-denied` attribute is where the reason lives either way.
    const create = screen.getByRole("button", { name: "New key" })
    expect(create.getAttribute("aria-disabled")).toBe("true")
    expect(create.getAttribute("data-denied")).toBe("needs platform-admin")
    expect(create.hasAttribute("disabled")).toBe(false)

    const revoke = screen.getByRole("button", { name: "Revoke key cmk_4e9c" })
    expect(revoke.getAttribute("aria-disabled")).toBe("true")
    expect(revoke.getAttribute("data-denied")).toBe("needs platform-admin")
    expect(revoke.hasAttribute("disabled")).toBe(false)

    fireEvent.click(revoke)
    expect(screen.queryByText("Revoke this key?")).toBeNull()
  })
})
