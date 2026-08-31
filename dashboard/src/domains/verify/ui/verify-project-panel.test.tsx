import { createContext, useContext } from "react"
import type { ReactNode } from "react"
import { cleanup, fireEvent, render, screen } from "@testing-library/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { beforeAll, describe, expect, it, vi } from "vitest"

import type {
  VerifyCommand,
  VerifyProject,
} from "@/domains/verify/model/types"
import { VerifyProjectPanel } from "@/domains/verify/ui/verify-project-panel"

/* The virtualizer needs a scroll port with a depth and something watching it,
   and jsdom has neither — without these the body renders no rows at all and
   every assertion below would pass by looking at an empty table. */
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

/* A result deep-links to the run that produced it, so the panel only renders
   inside a router. A memory router carrying the product's own paths keeps this
   off the app's generated route tree. */
const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/runs/$runId"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const PROJECT: VerifyProject = {
  projectId: "p_test",
  enabled: true,
  source: {
    repo: "here/web-app",
    ref: "main",
    path: ".comuki/verify.yaml",
    url: "https://github.com/here/web-app/blob/main/.comuki/verify.yaml",
  },
  readAt: "6 min ago",
}

const COMMANDS: VerifyCommand[] = [
  {
    id: "vc_types",
    projectId: "p_test",
    path: ".comuki/verify.yaml",
    name: "types",
    command: "bun run typecheck",
    last: {
      outcome: "success",
      runId: "b3d8a402",
      at: "11 min ago",
      durationSec: 34,
    },
  },
  {
    id: "vc_unit",
    projectId: "p_test",
    path: ".comuki/verify.yaml",
    name: "unit",
    command: "bun run test",
    last: {
      outcome: "failed",
      runId: "5b1d7e40",
      at: "11 min ago",
      durationSec: 128,
      detail: "2 failed in profile-flow.test.ts — expected 7 columns, got 6",
    },
  },
  {
    id: "vc_visual",
    projectId: "p_test",
    path: ".comuki/verify.yaml",
    name: "visual baseline",
    command: "bun run test:visual",
    last: null,
  },
]

/* The router mounts its tree asynchronously, so every case waits for the
   panel's own heading before it looks at anything. */
async function mount({
  project = PROJECT,
  commands = COMMANDS,
  denied = null as string | null,
} = {}) {
  const onEnabledChange = vi.fn()
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  render(
    <SlotContext
      value={
        <VerifyProjectPanel
          project={project}
          projectKey="test"
          projectName="Test project"
          commands={commands}
          denied={denied}
          saving={false}
          onEnabledChange={onEnabledChange}
        />
      }
    >
      <RouterProvider router={router} />
    </SlotContext>
  )

  await screen.findByRole("heading", { level: 2 })

  return { onEnabledChange }
}

describe("the screen says where the commands live", () => {
  it("names the repo, the ref and the path, precisely", async () => {
    await mount()

    const path = document.querySelector('[data-test="verify-source-path"]')
    // The coordinate an operator can act on, in one line and in that order,
    // because that is the order it narrows.
    expect(path?.textContent).toContain("here/web-app @ main")
    expect(path?.textContent).toContain(".comuki/verify.yaml")
    expect(path?.textContent).toContain("read 6 min ago")
  })

  it("links out to the file rather than offering to edit it", async () => {
    await mount()

    const link = document.querySelector(
      '[data-test="verify-source-link"]'
    ) as HTMLAnchorElement
    expect(link.getAttribute("href")).toBe(PROJECT.source.url)
    expect(link.getAttribute("target")).toBe("_blank")
    // Three words became a glyph, so what the link says is now what it is
    // named — the tooltip repeats it for a sighted pointer or keyboard, but
    // the name is the reading that is always there.
    expect(link.getAttribute("aria-label")).toBe("Open in git")
  })

  it("says editing is a commit, not a feature that is missing", async () => {
    await mount()

    expect(
      screen.getByText(
        /Changing one is a commit in their repository — that is what makes a run reproducible, so there is no editor on this screen by design\./
      )
    ).toBeTruthy()
  })

  it("offers no edit control on any row", async () => {
    await mount()

    // This project has shipped the alternative once: a panel titled
    // "RulesEditor" over a table nobody could edit. The affordance is absent
    // rather than disabled, because a disabled Edit describes a feature the
    // product deliberately does not have.
    const table = document.querySelector('[data-test="data-table"]')
    const buttons = [...(table?.querySelectorAll("button") ?? [])].map((node) =>
      (node.getAttribute("aria-label") ?? node.textContent ?? "").toLowerCase()
    )
    for (const label of buttons) {
      expect(label).not.toContain("edit")
      expect(label).not.toContain("delete")
      expect(label).not.toContain("save")
    }
  })
})

describe("what each check last said", () => {
  it("shows the command line verbatim", async () => {
    await mount()
    expect(screen.getByText("bun run typecheck")).toBeTruthy()
    expect(screen.getByText("bun run test")).toBeTruthy()
  })

  it("tells a failure and a check nothing has reached apart", async () => {
    await mount()

    const outcomes = [
      ...document.querySelectorAll('[data-test="verify-result"]'),
    ].map((node) => node.getAttribute("data-outcome"))

    // Three readings, not two: "never ran" is a hole in the gate's coverage and
    // "failed" is a fact about the code, and rolling them together would hide
    // the one nobody would otherwise notice.
    expect(outcomes).toContain("passed")
    expect(outcomes).toContain("failed")
    expect(outcomes).toContain("never")

    // The word is in the badge itself, not carried by colour alone. (The
    // toolbar's own filter offers the same word, which is why this looks at
    // the badge rather than at the page.)
    const badges = [
      ...document.querySelectorAll('[data-test="verify-result"]'),
    ].map((node) => node.textContent)
    expect(badges).toContain("never ran")
    expect(screen.getByText("no run has reached this check")).toBeTruthy()
  })

  it("puts the failure's first line on the row", async () => {
    await mount()
    expect(
      screen.getByText(
        "2 failed in profile-flow.test.ts — expected 7 columns, got 6"
      )
    ).toBeTruthy()
  })

  it("deep-links a result to the run that produced it", async () => {
    await mount()

    const links = [
      ...document.querySelectorAll('[data-test="verify-run-link"]'),
    ] as HTMLAnchorElement[]

    expect(links.map((link) => link.textContent)).toEqual([
      "b3d8a402",
      "5b1d7e40",
    ])
    expect(links[0].getAttribute("href")).toBe("/runs/b3d8a402")
  })
})

describe("the gate as a feature flag", () => {
  it("still lists the commands when the gate is off", async () => {
    await mount({ project: { ...PROJECT, enabled: false } })

    // A switch here does not delete a file over there, and hiding the list
    // would suggest it had.
    expect(screen.getByText("bun run typecheck")).toBeTruthy()
    expect(
      document.querySelector('[data-test="verify-gate-off"]')?.textContent
    ).toContain("The file is still in git")
  })

  it("carries the switch state in a word as well as a position", async () => {
    await mount()
    expect(screen.getByText("gate on")).toBeTruthy()
    cleanup()

    await mount({ project: { ...PROJECT, enabled: false } })
    expect(screen.getByText("gate off")).toBeTruthy()
  })

  it("flips for a role that may turn it", async () => {
    const { onEnabledChange } = await mount()

    fireEvent.click(
      document.querySelector('[data-test="verify-enabled"]') as HTMLInputElement
    )
    expect(onEnabledChange).toHaveBeenCalledWith(false)
  })

  it("keeps the switch and explains it for a role that may not", async () => {
    const { onEnabledChange } = await mount({
      denied: "needs project-admin, operator or platform-admin on other",
    })

    const toggle = document.querySelector(
      '[data-test="verify-enabled"]'
    ) as HTMLInputElement

    expect(toggle.getAttribute("aria-disabled")).toBe("true")
    expect(toggle.getAttribute("title")).toBe(
      "needs project-admin, operator or platform-admin on other"
    )
    // `denied`, never `disabled`: a disabled control fires no pointer events,
    // so the sentence explaining it would be unreachable.
    expect(toggle.hasAttribute("disabled")).toBe(false)

    fireEvent.click(toggle)
    expect(onEnabledChange).not.toHaveBeenCalled()
  })
})

describe("a project whose git declares nothing", () => {
  it("names the file the client is expected to create", async () => {
    await mount({ commands: [] })

    const empty = document.querySelector('[data-test="verify-empty"]')
    expect(empty?.textContent).toContain("No checks are declared")
    expect(empty?.textContent).toContain(".comuki/verify.yaml")
    expect(empty?.textContent).toContain("here/web-app")
    expect(empty?.textContent).toContain("there is nowhere here to create one")

    expect(
      (
        document.querySelector(
          '[data-test="verify-empty-link"]'
        ) as HTMLAnchorElement
      ).getAttribute("href")
    ).toBe(PROJECT.source.url)
  })
})
