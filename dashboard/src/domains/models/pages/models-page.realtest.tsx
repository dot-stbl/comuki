import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { TestSession } from "@/shared/session/test-session"
import type { Role } from "@/shared/session"

import { getApiV1ProxyKeys } from "@/shared/api/_generated/clients/getApiV1ProxyKeys"
import { ModelsPage } from "./models-page"

/* Real mode: the spend-key catalogue is its own query, and the whole point
   of this file is what the screen does when that query fails — the shipped
   behaviour drew an empty table indistinguishable from "no keys". */
vi.mock("@/shared/config/env", () => ({
  env: {
    useMock: false,
    apiBaseUrl: "http://localhost",
    proxyKey: "test-key",
    oidcProvider: null,
    repoUrl: null,
    commitSha: "",
    deployEnv: "local",
  },
}))

/* The /v1/models adapter is outside the kubb document; hand-mocked so the
   models query resolves without a network. */
vi.mock("@/shared/api/models-proxy", () => ({
  fetchProxyModelsAsync: vi.fn().mockResolvedValue({
    object: "list",
    data: [{ id: "lead-xl-2", object: "model", created: 0, owned_by: "comuki" }],
  }),
}))

vi.mock("@/shared/api/_generated/clients/getApiV1ProxyKeys", () => ({
  getApiV1ProxyKeys: vi.fn(),
}))

/* Same harness contracts as models-page.test.tsx: the shell's pane group
   cannot solve for a layout in jsdom, and the virtualizer needs a port
   with depth. */
vi.mock("react-resizable-panels", () => ({
  Group: ({
    children,
    className,
  }: {
    children: ReactNode
    className?: string
  }) => (
    <div className={className} data-test="split-pane">
      {children}
    </div>
  ),
  Panel: ({
    children,
    className,
    id,
  }: {
    children: ReactNode
    className?: string
    id?: string
  }) => (
    <div className={className} data-panel={id}>
      {children}
    </div>
  ),
  Separator: ({ className }: { className?: string }) => (
    <div role="separator" className={className} data-test="split-separator" />
  ),
}))

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
    value: 1400,
  })
})

/** The transport's rejection shape: an Error carrying the host's problem body. */
function catalogueDown(): Error {
  return Object.assign(
    new Error("request failed 503"),
    {
      status: 503,
      data: {
        title: "catalogue unavailable",
        detail: "the proxy key store is unreachable",
      },
    },
  )
}

/** One valid catalogue row, so the success control is a real answer. */
function catalogueUp() {
  return {
    items: [
      {
        id: "a3f8c2e1d4b65f0791ac33e8aa6b1c4d5e7f9012a3b4c5d6e7f8012345678ab",
        prefix: "ck_live_A3F8",
        projectId: "b3d8a402-1111-2222-3333-444444444444",
        provider: "openai",
        baseUrl: "https://api.openai.com/v1",
        defaultModel: "gpt-5.2",
        allowedModels: ["gpt-5.2"],
        budgetUsd: 400,
        expiresAt: null,
      },
    ],
  }
}

const rootRoute = createRootRoute({ component: ModelsPage })
const blank = () => null
const routeTree = rootRoute.addChildren(
  [
    "/tasks",
    "/runs",
    "/queue",
    "/approvals",
    "/cost",
    "/sources",
    "/knowledge",
    "/verify",
    "/settings",
    "/projects",
    "/identity",
    "/compute",
    "/models",
    "/observability",
    "/components",
    "/login",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function renderScreen(roles: Role[] = ["platform-admin"]) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles}>
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          <RouterProvider router={router} />
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const find = (selector: string) => document.querySelector(selector)

beforeEach(() => {
  vi.mocked(getApiV1ProxyKeys).mockReset()
})

describe("the spend-key catalogue, over the wire", () => {
  it("says the catalogue failed rather than drawing an empty table", async () => {
    vi.mocked(getApiV1ProxyKeys).mockRejectedValue(catalogueDown())
    renderScreen()

    const alert = await waitFor(() => {
      const panel = find('[role="alert"]')
      expect(panel).not.toBeNull()
      return panel as HTMLElement
    })

    expect(alert.textContent).toContain("Couldn't load the spend keys")
    // The host's own sentence, not "request failed 503" for an operator to
    // translate.
    expect(alert.textContent).toContain(
      "the proxy key store is unreachable",
    )
    // The table is not drawn: an empty Spend-keys table would read as "no
    // keys", which is a different lie than the one this panel tells.
    expect(find('[data-test="models-keys"]')).toBeNull()
  })

  it("draws the sections when the catalogue answers", async () => {
    vi.mocked(getApiV1ProxyKeys).mockResolvedValue(catalogueUp())
    renderScreen()

    await waitFor(() => {
      expect(find('[data-test="models-keys"]')).not.toBeNull()
    })
    expect(find('[role="alert"]')).toBeNull()
  })
})
