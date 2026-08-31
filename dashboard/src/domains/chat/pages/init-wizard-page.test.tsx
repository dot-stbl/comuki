import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { InitWizardPage } from "@/domains/chat/pages/init-wizard-page"
import { stepFrom, type InitStep } from "@/domains/chat/model/init-wizard"
import { PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider, type SessionUser } from "@/shared/session"
import { setSelectValue } from "@/shared/ui/select/test-select"

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
    value: 1200,
  })
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

const all = (name: string) =>
  Array.from(document.querySelectorAll<HTMLElement>(`[data-test="${name}"]`))

const RAIL_PATHS = [
  "/",
  "/tasks",
  "/runs",
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
  "/chat",
]

interface Search {
  step?: InitStep
  project?: string
}

function mount(entries: string[], user: SessionUser = SESSION_USER_SEED) {
  const rootRoute = createRootRoute()
  const blank = () => null

  const init = createRoute({
    getParentRoute: () => rootRoute,
    path: "/chat/init",
    validateSearch: (search: Record<string, unknown>): Search => {
      const parsed: Search = {}
      if (typeof search.step === "string") {
        parsed.step = stepFrom(search.step)
      }
      if (typeof search.project === "string" && search.project) {
        parsed.project = search.project
      }
      return parsed
    },
    component: function InitRoute() {
      const { step = "repo", project } = init.useSearch()
      return <InitWizardPage step={step} project={project} />
    },
  })

  const router = createRouter({
    routeTree: rootRoute.addChildren([
      ...RAIL_PATHS.map((path) =>
        createRoute({ getParentRoute: () => rootRoute, path, component: blank })
      ),
      init,
    ]),
    history: createMemoryHistory({ initialEntries: entries }),
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

const here = (router: ReturnType<typeof mount>) =>
  `${router.state.location.pathname}${router.state.location.searchStr}`

describe("the wizard is a routed flow, not a modal", () => {
  it("puts the step in the address, so back and reload both work", async () => {
    const router = mount(["/chat/init?step=compute"])

    await screen.findByRole("heading", { name: "Onboard a repository" })
    expect(here(router)).toBe("/chat/init?step=compute")
    // The step the address named is the step showing.
    expect(
      all("init-step").find(
        (node) => node.getAttribute("aria-current") === "step"
      )?.getAttribute("data-step")
    ).toBe("compute")
  })

  it("falls back to the first step when the address names nonsense", async () => {
    mount(["/chat/init?step=banana"])
    await screen.findByRole("heading", { name: "Onboard a repository" })
    expect(
      all("init-step").find(
        (node) => node.getAttribute("aria-current") === "step"
      )?.getAttribute("data-step")
    ).toBe("repo")
  })

  it("has a path back to the console, as a real link", async () => {
    mount(["/chat/init"])
    await screen.findByRole("heading", { name: "Onboard a repository" })
    expect(
      screen.getByRole("link", { name: "console" }).getAttribute("href")
    ).toBe("/chat")
  })
})

describe("the first step decides which project is being onboarded", () => {
  it("offers only the projects this shift may connect a source on", async () => {
    // The seeded shift administers `p_atlas` and nothing else — the same rule
    // and the same permission the composer's scope chip uses.
    mount(["/chat/init"])
    const trigger = await waitFor(() => {
      const found = at("init-project")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })

    const options = Array.from(
      trigger.parentElement?.querySelectorAll("option") ?? []
    )
      .map((option) => option.value)
      .filter(Boolean)
    expect(options).toEqual(["p_atlas"])
  })

  it("takes the project the console scoped it to", async () => {
    mount(["/chat/init?project=p_atlas"])
    const trigger = await waitFor(() => {
      const found = at("init-project")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(
      trigger.parentElement?.querySelector("select")?.value
    ).toBe("p_atlas")
  })

  it("ignores a project pasted into the address that this shift cannot touch", async () => {
    mount(["/chat/init?project=p_plexor"])
    const trigger = await waitFor(() => {
      const found = at("init-project")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(trigger.parentElement?.querySelector("select")?.value).toBe("")
  })
})

describe("a step stops the operator where it can still be fixed", () => {
  it("refuses to advance with the questions unanswered, and says which", async () => {
    const router = mount(["/chat/init"])
    await screen.findByRole("heading", { name: "Onboard a repository" })

    fireEvent.click(screen.getByRole("button", { name: "Continue" }))

    await waitFor(() =>
      expect(
        all("field-error").map((node) => node.textContent)
      ).toEqual([
        "choose the project this repository belongs to",
        "a git remote is required",
      ])
    )
    expect(here(router)).toBe("/chat/init")
  })

  it("advances once the step is answered", async () => {
    const router = mount(["/chat/init"])
    await screen.findByRole("heading", { name: "Onboard a repository" })

    setSelectValue(at("init-project") as HTMLElement, "p_atlas")
    fireEvent.change(screen.getByLabelText("Git remote"), {
      target: { value: "git@github.com:acme/checkout-web.git" },
    })
    fireEvent.click(screen.getByRole("button", { name: "Continue" }))

    await waitFor(() => expect(here(router)).toBe("/chat/init?step=compute"))
  })
})

describe("the confirm step", () => {
  it("says nothing has been created yet, and starts a stream when pressed", async () => {
    mount(["/chat/init?step=confirm"])
    await screen.findByRole("heading", { name: "Onboard a repository" })

    expect(at("init-review")).not.toBeNull()
    // Nothing is created by arriving here. The submit keeps its words.
    expect(at("init-stream")).toBeNull()

    fireEvent.click(screen.getByRole("button", { name: "Start onboarding" }))
    await waitFor(() => expect(at("init-stream")).not.toBeNull())
    expect(all("init-stage").length).toBeGreaterThan(0)
  })
})

describe("a shift that may not connect a source", () => {
  it("is refused on the submit, with the roles that would work", async () => {
    mount(["/chat/init?step=confirm"], {
      ...SESSION_USER_SEED,
      platformRoles: ["viewer"],
      projectRoles: {},
    })
    await screen.findByRole("heading", { name: "Onboard a repository" })

    const submit = screen.getByRole("button", { name: "Start onboarding" })
    expect(submit.getAttribute("aria-disabled")).toBe("true")
    expect(submit.getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin"
    )
    expect(submit.hasAttribute("disabled")).toBe(false)

    fireEvent.click(submit)
    expect(at("init-stream")).toBeNull()
  })
})
