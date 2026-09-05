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
import { ADMISSION_MODES } from "@/domains/sources/model/providers"
import { SourceDetailPage } from "@/domains/sources/pages/source-detail-page"
import { PROJECTS_SEED } from "@/shared/api/mock/session.seed"
import {
  readSeedSources,
  resetSeedSources,
} from "@/shared/api/mock/sources.store"
import { SessionProvider, type Role } from "@/shared/session"

/**
 * A source's own page, and the two dialogs that folded into it.
 *
 * `watch-dialog` is the second region and `connect-source-dialog`'s edit half
 * is the third, so every assertion those two files made about *the decisions*
 * is made here instead: the three admission modes and their sentences, the
 * filter expression that is never parsed, the write-back preview, the switch
 * and the save that stay visible and explain themselves when a role may not
 * use them.
 *
 * What is new is what a page has and a modal could not: an address that can be
 * stale, acts on the record standing apart from acts on a draft, and hand-offs
 * that link rather than redraw.
 */

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

  /* jsdom measures nothing, so the shell rail's persisted split layout is
     meaningless and restoring it on a second mount throws. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  resetSeedSources()
})

const RAIL_PATHS = [
  "/",
  "/chat",
  "/tasks",
  "/runs",
  "/queue",
  "/approvals",
  "/cost",
  "/knowledge",
  "/verify",
  "/settings",
  "/identity",
  "/compute",
  "/models",
  "/observability",
  "/components",
]

/** The seeded connections this file works on, by the id the address names. */
const GITHUB = "src_gh_comuki"
const GITLAB = "src_gl_plexor"
const JIRA = "src_jira_atlas"
const NATIVE = "src_native_comuki"

interface Search {
  q?: string
}

function buildRouteTree() {
  const rootRoute = createRootRoute()
  const blank = () => null

  const sources = createRoute({
    getParentRoute: () => rootRoute,
    path: "/sources",
    validateSearch: (search: Record<string, unknown>): Search =>
      typeof search.q === "string" && search.q ? { q: search.q } : {},
    component: blank,
  })

  const detail = createRoute({
    getParentRoute: () => rootRoute,
    path: "/sources/$sourceId",
    component: function DetailRoute() {
      const { sourceId } = detail.useParams()
      return <SourceDetailPage sourceId={sourceId} />
    },
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    createRoute({
      getParentRoute: () => rootRoute,
      path: "/projects/$projectId",
      component: blank,
    }),
    createRoute({
      getParentRoute: () => rootRoute,
      path: "/sources/$sourceId/ticket/new",
      component: blank,
    }),
    sources,
    detail,
  ])
}

function mount(
  entries: string[],
  roles: Role[] = ["platform-admin"],
  projectRoles: Record<string, Role[]> = {}
) {
  const router = createRouter({
    routeTree: buildRouteTree(),
    history: createMemoryHistory({ initialEntries: entries }),
  })

  /* The *seeded* projects rather than a pair invented for the test: every
     denial sentence on this page names the project by the key the operator
     calls it, and the connections all live on the seeded three. */
  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <SessionProvider
        user={{
          id: "u_test",
          name: "Test User",
          email: "test@comuki.local",
          platformRoles: roles,
          projectRoles,
        }}
        projects={PROJECTS_SEED}
      >
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

const control = (testId: string) =>
  document.querySelector(`[data-test="${testId}"]`) as HTMLElement

const controls = (testId: string) => [
  ...document.querySelectorAll(`[data-test="${testId}"]`),
]

/** Open a source's page, from the list, and wait for it to arrive. */
async function open(
  sourceId: string,
  roles: Role[] = ["platform-admin"],
  projectRoles: Record<string, Role[]> = {},
  entries: string[] = ["/sources", `/sources/${sourceId}`]
) {
  const router = mount(entries, roles, projectRoles)
  await waitFor(() => expect(control("source-facts")).not.toBeNull(), {
    timeout: 3000,
  })
  return router
}

const filterBox = () =>
  control("filter-expression") as unknown as HTMLTextAreaElement

const savedWatch = (id: string) =>
  readSeedSources().connections.find((entry) => entry.id === id)?.watch

const savedConnection = (id: string) =>
  readSeedSources().connections.find((entry) => entry.id === id)

function saveWatch() {
  fireEvent.click(control("watch-submit"))
}

/* ---------------------------------------------------------------------- *
 * The watch form — everything `watch-dialog` decided, on the page it
 * landed on.
 * ---------------------------------------------------------------------- */

describe("the three admission modes are told apart by reading, not by guessing", () => {
  it("gives each mode its own sentence", async () => {
    await open(GITHUB)

    const boxes = controls("admission-option")
    expect(boxes).toHaveLength(3)
    expect(boxes.map((box) => box.getAttribute("data-value"))).toEqual([
      "watch",
      "inbox-only",
      "both",
    ])

    // Three different products for the same ticket, and the difference is who
    // moves next. Three bare words would be asking the operator which.
    const sentences = ADMISSION_MODES.map((mode) => mode.description)
    expect(new Set(sentences).size).toBe(3)
    for (const sentence of sentences) {
      expect(screen.getByText(sentence)).toBeTruthy()
    }

    expect(screen.getByText(/a matching ticket starts a run/)).toBeTruthy()
    expect(screen.getByText(/waits for a person to take it/)).toBeTruthy()
    expect(screen.getByText(/stays in the catalog/)).toBeTruthy()
  })

  it("marks the chosen one, and only that one", async () => {
    await open(GITHUB)

    const radios = screen.getAllByRole("radio") as HTMLInputElement[]
    expect(radios.filter((radio) => radio.checked)).toHaveLength(1)
    // `src_gh_comuki` arrives from the seed admitting into both.
    expect(radios.find((radio) => radio.checked)?.getAttribute("value")).toBe(
      "both"
    )

    // The box the operator actually sees carries the selection too, driven by
    // the component rather than by a parent selector — so the border, the wash
    // and the tick cannot disagree with the radio underneath them.
    const selected = controls("admission-option").filter(
      (box) => box.getAttribute("data-selected") === "true"
    )
    expect(selected).toHaveLength(1)
    expect(selected[0].getAttribute("data-value")).toBe("both")
  })

  it("saves the mode that was picked", async () => {
    await open(GITHUB)

    const radios = screen.getAllByRole("radio") as HTMLInputElement[]
    fireEvent.click(radios.find((radio) => radio.value === "watch")!)
    saveWatch()

    await waitFor(() => expect(savedWatch(GITHUB)?.mode).toBe("watch"), {
      timeout: 3000,
    })
  })

  it("keeps every mode reachable by keyboard, in one radio group", async () => {
    await open(GITHUB)

    const radios = screen.getAllByRole("radio") as HTMLInputElement[]
    // One `name`, so the arrow keys move between them and the group is a
    // single tab stop — the whole reason these are real radios under the boxes.
    expect(new Set(radios.map((radio) => radio.name))).toEqual(
      new Set(["admission"])
    )
  })
})

describe("the filter expression is stored verbatim and never parsed", () => {
  it("says so on the field, permanently", async () => {
    await open(GITHUB)

    expect(
      screen.getByText(
        /stored verbatim and not parsed — the filter language is not decided yet/
      )
    ).toBeTruthy()
  })

  it("puts what was typed into the store byte for byte", async () => {
    await open(GITHUB)

    /* Deliberately ambiguous under every candidate grammar: leading and
       trailing whitespace, a newline, two separators, an unbalanced quote, a
       colon, a trailing comma and a trailing space. Nothing between the
       textarea and the store may tokenise, trim, normalise or reject any of
       it — the language is undecided, and a UI that quietly picked one would
       be deciding it by accident. */
    const messy =
      '  labels: swarm, area/dashboard\nprojects = web-app,  "half \n\ttrailing: yes, '
    fireEvent.change(filterBox(), { target: { value: messy } })
    saveWatch()

    await waitFor(() => expect(savedWatch(GITHUB)?.filter).toBe(messy), {
      timeout: 3000,
    })
    // Said as bluntly as it can be said: not "equal after trimming".
    expect(savedWatch(GITHUB)?.filter).toHaveLength(messy.length)
  })

  it("treats an empty expression as a value rather than an error", async () => {
    await open(GITHUB)

    fireEvent.change(filterBox(), { target: { value: "" } })

    // No syntax error, because there is no syntax to be wrong about — and the
    // save stays open, because "everything this connection can see" is a real
    // thing to mean.
    expect(control("field-error")).toBeNull()
    expect((control("watch-submit") as HTMLButtonElement).disabled).toBe(false)

    saveWatch()
    await waitFor(() => expect(savedWatch(GITHUB)?.filter).toBe(""), {
      timeout: 3000,
    })
  })

  it("offers field names as hints and inserts nothing but the name", async () => {
    await open(GITHUB)

    fireEvent.change(filterBox(), { target: { value: "" } })
    const hints = controls("filter-hint") as HTMLButtonElement[]

    // The provider's own nouns, and no operators anywhere among them.
    expect(hints.map((hint) => hint.textContent)).toEqual([
      "labels",
      "repo",
      "assignee",
      "milestone",
      "state",
    ])

    fireEvent.click(hints[0])
    // A bare word: no colon, no equals, no quotes. Inserting punctuation would
    // be asserting a separator, and a separator asserted by an affordance is
    // how a language gets decided by accident.
    expect(filterBox().value).toBe("labels")

    fireEvent.click(hints[1])
    expect(filterBox().value).toBe("labels\nrepo")

    // And what a hint appended is stored exactly as it stands, punctuation-free
    // and all.
    saveWatch()
    await waitFor(() => expect(savedWatch(GITHUB)?.filter).toBe("labels\nrepo"), {
      timeout: 3000,
    })
  })

  it("asks a different provider for different nouns", async () => {
    await open(JIRA)

    const hints = controls("filter-hint").map((hint) => hint.textContent)

    expect(hints).toContain("jql")
    expect(hints).not.toContain("repo")
  })
})

describe("turning a watch on says what the tracker will start saying", () => {
  it("previews the write-back in the provider's own words", async () => {
    await open(GITHUB)

    const preview = control("status-mapping")
    expect(preview?.textContent).toContain("label comuki:running")
    expect(preview?.textContent).toContain("close the issue")
    expect(preview?.textContent).toContain("status written back to github")
  })

  it("says plainly that native has nowhere to write back to", async () => {
    await open(NATIVE)

    expect(screen.getByText(/native intake is the tracker/)).toBeTruthy()
    // And the other half of the same fact: there is no watch to turn on either.
    expect(control("native-no-watch")).not.toBeNull()
    expect(control("watch-submit")).toBeNull()
  })
})

describe("a watch this session may not change", () => {
  it("keeps the switch and the save, and explains both", async () => {
    // A viewer on `p_comuki`, which is the row above the one they administer on
    // every list in this product.
    await open(GITHUB, ["viewer"], { p_comuki: ["viewer"] })

    const save = control("watch-submit") as HTMLButtonElement
    expect(save.getAttribute("aria-disabled")).toBe("true")
    expect(save.getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin on comuki"
    )
    expect(save.getAttribute("title")).toBe(
      "needs project-admin or platform-admin on comuki"
    )
    // `denied` and never `disabled`: a disabled control fires no pointer
    // events, so the sentence explaining it would be unreachable.
    expect(save.hasAttribute("disabled")).toBe(false)

    const toggle = control("watch-enabled") as HTMLInputElement
    expect(toggle.getAttribute("aria-disabled")).toBe("true")
    const before = savedWatch(GITHUB)?.filter

    fireEvent.click(toggle)
    saveWatch()

    // Nothing moved.
    expect(savedWatch(GITHUB)?.enabled).toBe(true)
    expect(savedWatch(GITHUB)?.filter).toBe(before)
  })
})

/* ---------------------------------------------------------------------- *
 * The page itself.
 * ---------------------------------------------------------------------- */

describe("an address that names a connection can be stale", () => {
  it("names the id it was given and says how somebody gets here", async () => {
    mount(["/sources", "/sources/src_vanished"])

    await screen.findByText("No connection with that id")

    // The id is on the page, because the operator is going to compare it with
    // whatever they pasted.
    expect(control("source-not-found").textContent).toContain("src_vanished")
    // Disconnecting is something this section does, so arriving here is
    // ordinary rather than an error.
    expect(control("source-not-found").textContent).toMatch(
      /disconnected — here or in another tab — is the ordinary way/
    )
    expect(
      screen.getByRole("link", { name: "Back to sources" }).getAttribute("href")
    ).toBe("/sources")
    // No form offering to configure something that is not there.
    expect(control("watch-submit")).toBeNull()
    expect(control("connection-submit")).toBeNull()
  })
})

describe("what the page says about the connection itself", () => {
  it("shows the provider, the credential and the env-var that holds it", async () => {
    await open(GITLAB)

    const facts = control("source-facts")
    // The provider is its mark, exactly as it is in the list's provider column
    // — and the mark keeps the word as its accessible name, so a reader who
    // does not recognise the glyph still gets the noun.
    const mark = facts.querySelector("[data-test='brand-tag']")
    expect(mark?.getAttribute("data-brand")).toBe("gitlab")
    expect(screen.getByRole("img", { name: "gitlab" })).toBeTruthy()

    expect(facts.textContent).toContain("personal access token")
    // A self-hosted instance names its host; the credential says only the
    // env-var name — the value never reaches this surface.
    expect(facts.textContent).toContain("https://git.plexor.internal")
    expect(facts.textContent).toContain("svc-comuki")
    expect(facts.textContent).toContain("MOCK_PLEXOR_GITLAB_TOKEN")
    // The exact span of the credential sentence is what tells the operator
    // the env var is host-side rather than stored here.
    expect(screen.getByText(/resolved on the host/i)).toBeTruthy()

    // The title is the connection and the summary says whose it is.
    expect(
      (await screen.findByRole("heading", { level: 1 })).textContent
    ).toBe("plexor/identity-svc")
    expect(screen.getByText(/gitlab · plexor/)).toBeTruthy()
  })

  it("puts a broken connection's reason on the page, in the provider's words", async () => {
    await open(JIRA)

    // The badge says *that* it is broken; this is the only place that says why.
    expect(control("source-error").textContent).toContain(
      "the api token was revoked on 24 aug"
    )
    expect(control("connection-state")?.getAttribute("data-state")).toBe(
      "error"
    )
  })

  it("says a cloud provider has no instance to name", async () => {
    await open(GITHUB)

    expect(control("source-facts").textContent).toContain("cloud")
  })
})

describe("the acts on the record ride in the header", () => {
  it("keeps the badge, the probe and the disconnect out of the two footers", async () => {
    await open(GITHUB)

    const header = document.querySelector(
      "[data-test='page-header']"
    ) as HTMLElement
    expect(header.querySelector("[data-test='connection-state']")).not.toBeNull()
    expect(header.querySelector("[data-test='source-test']")).not.toBeNull()
    expect(header.querySelector("[data-test='source-disconnect']")).not.toBeNull()

    // Disconnecting is not a way of saving a draft, so it is not beside a save.
    expect(header.querySelector("[data-test='watch-submit']")).toBeNull()
    expect(header.querySelector("[data-test='connection-submit']")).toBeNull()
  })

  it("refuses to disconnect native intake, and says whose refusal it is", async () => {
    await open(NATIVE, ["platform-admin"])

    const button = control("source-disconnect")
    // Present, in the same place, at the same size — hiding it would leave the
    // operator wondering whether they simply lack the role.
    expect(button).not.toBeNull()
    expect(button.getAttribute("aria-disabled")).toBe("true")
    expect(button.getAttribute("data-denied")).toBe(
      "native intake cannot be disconnected — it is the product's own way of accepting a ticket"
    )
    // `disabled` would drop it out of the tab order and kill the hover that
    // carries the sentence.
    expect(button.hasAttribute("disabled")).toBe(false)

    fireEvent.click(button)
    expect(control("confirm-dialog")).toBeNull()
    // And the store says the same thing, which is the half no refactor can
    // delete by touching a button.
    expect(
      readSeedSources().connections.some((entry) => entry.id === NATIVE)
    ).toBe(true)
  })

  it("leaves for the list once the record this page is about is gone", async () => {
    const router = await open(GITHUB)

    fireEvent.click(control("source-disconnect"))
    fireEvent.click(control("confirm-dialog-confirm"))

    await waitFor(() => expect(here(router)).toBe("/sources"), {
      timeout: 3000,
    })
    expect(
      readSeedSources().connections.some((entry) => entry.id === GITHUB)
    ).toBe(false)
  })

  it("explains every act to a role that may not perform any of them", async () => {
    await open(GITHUB, ["viewer"], { p_comuki: ["viewer"] })

    for (const testId of ["source-test", "source-disconnect"]) {
      const button = control(testId)
      expect(document.body.contains(button)).toBe(true)
      expect(button.getAttribute("aria-disabled")).toBe("true")
      expect(button.getAttribute("data-denied")).toBe(
        "needs project-admin or platform-admin on comuki"
      )
      expect(button.hasAttribute("disabled")).toBe(false)
      fireEvent.click(button)
    }

    // Nothing fired: no confirm opened and the connection is still there.
    expect(control("confirm-dialog")).toBeNull()
    expect(
      readSeedSources().connections.some((entry) => entry.id === GITHUB)
    ).toBe(true)
  })
})

describe("the connection region, which is where the connect dialog's edit half went", () => {
  it("offers the instance only where there is an instance to name", async () => {
    await open(GITHUB)
    expect(control("connection-base-url")).toBeNull()
    expect(control("connection-account")).not.toBeNull()
    expect(control("connection-auth")).not.toBeNull()
  })

  it("shows the base url of a self-hosted instance, ready to be changed", async () => {
    await open(GITLAB)
    expect((control("connection-base-url") as HTMLInputElement).value).toBe(
      "https://git.plexor.internal"
    )
  })

  it("will not save until the connection has answered, and forgets on an edit", async () => {
    await open(GITHUB)

    const save = control("connection-submit") as HTMLButtonElement
    expect(save.hasAttribute("disabled")).toBe(true)
    expect(save.hasAttribute("aria-disabled")).toBe(false)
    expect(control("probe-pending")).not.toBeNull()

    // One probe on this page, in the header, and this form reads its answer.
    fireEvent.click(control("source-test"))
    await waitFor(() => expect(control("probe-result")).not.toBeNull(), {
      timeout: 3000,
    })
    await waitFor(() => expect(save.hasAttribute("disabled")).toBe(false))

    fireEvent.change(control("connection-account"), {
      target: { value: "another-bot" },
    })
    // An instance that answered before the account was retyped is not evidence
    // about the account in the box now.
    expect(control("probe-result")).toBeNull()
    expect(save.hasAttribute("disabled")).toBe(true)
  })

  it("writes the account and the credential kind, and never a secret", async () => {
    await open(GITHUB)

    fireEvent.click(control("source-test"))
    await waitFor(() => expect(control("probe-result")).not.toBeNull(), {
      timeout: 3000,
    })

    fireEvent.change(control("connection-account"), {
      target: { value: "another-bot" },
    })
    fireEvent.click(control("source-test"))
    await waitFor(
      () =>
        expect(
          (control("connection-submit") as HTMLButtonElement).hasAttribute(
            "disabled"
          )
        ).toBe(false),
      { timeout: 3000 }
    )

    fireEvent.click(control("connection-submit"))

    await waitFor(
      () => expect(savedConnection(GITHUB)?.account).toBe("another-bot"),
      { timeout: 3000 }
    )
    // Structural, not stylistic: there is no field on a connection that could
    // hold a credential, and the patch that reaches the store has none either.
    expect(Object.keys(savedConnection(GITHUB)!)).not.toContain("secret")
    expect(savedConnection(GITHUB)?.secretStoredAt).toBe("2026-07-02")
  })

  it("is absent on native, which has nothing to reach", async () => {
    await open(NATIVE)
    expect(control("source-connection")).toBeNull()
    expect(control("connection-submit")).toBeNull()
  })
})

describe("the page links to the real screens rather than redrawing them", () => {
  it("hands the catalog off plainly rather than pretending to narrow it", async () => {
    await open(GITHUB)

    const handoff = control("handoff-tasks")
    expect(handoff.getAttribute("href")).toBe("/tasks")
    // No invented parameter: the catalog's search reads a ticket's title, id
    // and app, and a connection's name is none of those. Said out loud.
    expect(control("source-handoffs").textContent).toContain(
      "the catalog is not narrowed to this source"
    )
    // The count is the seeded watch's own reading.
    expect(control("source-handoffs").textContent).toContain("14")
  })

  it("points at the project this connection feeds", async () => {
    await open(GITHUB)

    expect(control("handoff-project").getAttribute("href")).toBe(
      "/projects/p_comuki"
    )
  })

  it("offers a ticket only where a ticket can be written", async () => {
    await open(NATIVE)
    expect(control("handoff-new-ticket").getAttribute("href")).toBe(
      `/sources/${NATIVE}/ticket/new`
    )

    // There is no ticket list on this page and no runs table: a detail page
    // links to the screens that own those rows.
    expect(control("data-table")).toBeNull()
  })

  it("keeps the ticket hand-off off a connection that has a remote end", async () => {
    await open(GITHUB)
    expect(control("handoff-new-ticket")).toBeNull()
  })
})

describe("leaving with something typed and not saved", () => {
  it("asks on an accidental departure", async () => {
    const router = await open(GITHUB)

    fireEvent.change(filterBox(), { target: { value: "labels: something" } })
    fireEvent.click(screen.getByRole("link", { name: "sources" }))

    await screen.findByText("Leave without saving?")
    expect(here(router)).toBe(`/sources/${GITHUB}`)

    fireEvent.click(screen.getByRole("button", { name: "Discard" }))
    await waitFor(() => expect(here(router)).toBe("/sources"))
  })

  it("never asks once the form has been put back where it was", async () => {
    const router = await open(GITHUB)

    fireEvent.change(filterBox(), { target: { value: "labels: something" } })
    // A cancel on a page that is *about* something is a revert, not a way out:
    // there is nowhere to go that is not this page.
    fireEvent.click(control("watch-cancel"))
    expect(filterBox().value).toBe(savedWatch(GITHUB)?.filter)

    fireEvent.click(screen.getByRole("link", { name: "sources" }))
    await waitFor(() => expect(here(router)).toBe("/sources"))
    expect(screen.queryByText("Leave without saving?")).toBeNull()
  })

  it("never asks after a save that landed", async () => {
    const router = await open(GITHUB)

    fireEvent.change(filterBox(), { target: { value: "labels: something" } })
    saveWatch()
    await waitFor(
      () => expect(savedWatch(GITHUB)?.filter).toBe("labels: something"),
      { timeout: 3000 }
    )

    fireEvent.click(screen.getByRole("link", { name: "sources" }))
    await waitFor(() => expect(here(router)).toBe("/sources"))
    expect(screen.queryByText("Leave without saving?")).toBeNull()
  })
})
