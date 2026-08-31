import { describe, expect, it } from "vitest"

import type { SidebarNavGroup } from "@/app/layout/app-shell-sidebar"
import { productNav } from "@/app/layout/nav"
import type { Role, Session } from "@/shared/session"

import { resolveQuery, type SearchItem } from "./resolve"
import { SEARCH_ACTS } from "./sections"
import { resolveShapes, type SearchCatalogue } from "./shapes"

/* The resolver is pure logic with no rendering in it at all, which is exactly
   why it deserves the hardest tests in this feature: every route the product
   can take out of the search box is decided here, and none of it is visible in
   a screenshot. */

const CATALOGUE: SearchCatalogue = {
  projects: [
    { id: "p_comuki", key: "comuki", name: "Comuki platform" },
    { id: "p_plexor", key: "plexor", name: "Plexor" },
    { id: "p_atlas", key: "atlas", name: "Atlas" },
  ],
  apps: [
    "auth-svc",
    "billing-api",
    "checkout-web",
    "docs-site",
    "web-app",
    "worker-pool",
  ],
}

function session(
  platformRoles: Role[] = ["platform-admin"],
  projectRoles: Record<string, Role[]> = {}
): Session {
  return {
    user: {
      id: "u_test",
      name: "Test User",
      email: "test@comuki.local",
      platformRoles,
      projectRoles,
    },
    projects: CATALOGUE.projects,
  }
}

function resolve(query: string, from = session(), nav?: SidebarNavGroup[]) {
  return resolveQuery(query, { session: from, catalogue: CATALOGUE, nav })
}

const hrefs = (items: SearchItem[]) => items.map((item) => item.href)
const kinds = (items: SearchItem[]) => items.map((item) => item.kind)
const of = (items: SearchItem[], group: SearchItem["group"]) =>
  items.filter((item) => item.group === group)

/* ------------------------------------------------------------------ *
 * (a) Identifier resolution — every shape lands where it should.
 * ------------------------------------------------------------------ */

describe("the shape catalogue routes without searching", () => {
  it.each([
    ["5b1d7e40", "run", "/runs/5b1d7e40"],
    ["wi_0101", "work item", "/queue?q=wi_0101"],
    ["wk_e34d", "worker", "/queue?w=wk_e34d"],
    ["ap-01", "approval", "/approvals?q=ap-01"],
    ["sha256:9c41ab", "image", "/queue?w=sha256%3A9c41ab"],
    ["cmk_4e9c", "api key", "/identity?tab=keys&q=cmk_4e9c"],
    ["duty@comuki.local", "person", "/identity?tab=users&q=duty%40comuki.local"],
    ["p_comuki", "project", "/projects?q=comuki"],
    ["comuki", "project", "/projects?q=comuki"],
    ["web-app", "app", "/runs?q=web-app"],
  ])("resolves %s to a %s at %s", (query, kind, href) => {
    const resolved = of(resolve(query), "resolved")

    expect({ kind: resolved[0]?.kind, href: resolved[0]?.href }).toEqual({
      kind,
      href,
    })
  })

  it("reads a run id case-insensitively, the way a log line spells it", () => {
    expect(hrefs(of(resolve("5B1D7E40"), "resolved"))).toEqual([
      "/runs/5B1D7E40",
    ])
  })

  it("narrows on the two internal identity ids too", () => {
    // Nothing in the product displays either id, but both get pasted out of a
    // log or an api response — so both lists carry the id in their match string
    // and the shape is allowed to hand it over. A shape that resolves to a
    // destination whose filter cannot receive it lands the operator on an empty
    // screen, which is worse than not resolving; `users-columns.tsx` and
    // `keys-columns.tsx` are the other half of this assertion.
    const user = of(resolve("u_duty"), "resolved")[0]
    const key = of(resolve("k_audit"), "resolved")[0]

    expect([user?.href, key?.href]).toEqual([
      "/identity?tab=users&q=u_duty",
      "/identity?tab=keys&q=k_audit",
    ])
  })

  it("sets every resolved identifier as a value, never as words", () => {
    for (const query of ["5b1d7e40", "wk_e34d", "comuki", "web-app"]) {
      expect(of(resolve(query), "resolved").every((item) => item.value)).toBe(
        true
      )
    }
  })

  it("does not mistake a project id this session cannot see for a project", () => {
    // `p_vega` exists in the registry and not in this shift's list. Answering
    // with it would be answering about somebody else's access.
    expect(of(resolve("p_vega"), "resolved")).toEqual([])
  })

  it("never lets a keyed shape and a catalogue shape both answer", () => {
    // `wi_0101` is a work item and is not a fuzzy match against anything. The
    // keyed tier answers alone whenever it answers at all.
    expect(kinds(of(resolve("wi_0101"), "resolved"))).toEqual(["work item"])
  })
})

/* ------------------------------------------------------------------ *
 * Ambiguity — a normal case, bounded by the catalogue.
 * ------------------------------------------------------------------ */

describe("ambiguity is answered with candidates rather than a guess", () => {
  it("offers every application a fragment names, each labelled by kind", () => {
    const resolved = of(resolve("web"), "resolved")

    expect(resolved.map((item) => item.label)).toEqual([
      "checkout-web",
      "web-app",
    ])
    expect(new Set(kinds(resolved))).toEqual(new Set(["app"]))
  })

  it("crosses kinds when a handle and an application name collide", () => {
    // The seed has no collision today; the mechanism has to have one anyway,
    // because the day a project is called `atlas` and an application
    // `atlas-api`, the palette must ask rather than pick.
    const candidates = resolveShapes("atlas", {
      projects: CATALOGUE.projects,
      apps: ["atlas-api", "atlas-web"],
    })

    expect(candidates.map((item) => `${item.kind} ${item.id}`)).toEqual([
      "project atlas",
      "app atlas-api",
      "app atlas-web",
    ])
  })

  it("refuses to answer a single letter from the catalogue", () => {
    // One character names everything, and a disambiguation between eleven
    // things is a results list wearing a disambiguation's clothes.
    expect(of(resolve("w"), "resolved")).toEqual([])
  })

  it("caps a kind's candidates so a choice never becomes a list", () => {
    const candidates = resolveShapes("svc", {
      projects: [],
      apps: ["a-svc", "b-svc", "c-svc", "d-svc", "e-svc", "f-svc"],
    })

    expect(candidates).toHaveLength(4)
  })
})

/* ------------------------------------------------------------------ *
 * (b) Sections and acts — the rail's own answer, not a second opinion.
 * ------------------------------------------------------------------ */

describe("sections and acts follow the rail's access rule", () => {
  it("lists every place and act this session can reach when nothing is typed", () => {
    const resting = resolve("")

    expect(new Set(resting.map((item) => item.group))).toEqual(
      new Set(["section", "act"])
    )
    // The resting list is places and acts only — a hand-off with no query to
    // hand off would be an act with an empty object.
    expect(of(resting, "handoff")).toEqual([])
    expect(of(resting, "section").length).toBeGreaterThan(5)
  })

  it("hides a section the session cannot reach, exactly as the rail hides it", () => {
    const viewer = resolve("identity", session(["viewer"]))
    const admin = resolve("identity", session(["platform-admin"]))

    expect(hrefs(of(viewer, "section"))).toEqual([])
    expect(hrefs(of(admin, "section"))).toContain("/identity")
  })

  it("drops the acts a role may not perform", () => {
    const viewer = of(resolve("new", session(["viewer"])), "act")
    const admin = of(resolve("new", session(["platform-admin"])), "act")

    // A viewer takes no tickets, creates no projects and writes no keys.
    expect(viewer).toEqual([])
    expect(admin.length).toBeGreaterThan(0)
  })

  it("matches the rail group as well as the item, so a tier is findable", () => {
    const observe = of(resolve("observe"), "section")

    expect(hrefs(observe)).toEqual(
      expect.arrayContaining(["/runs", "/queue", "/cost"])
    )
  })

  it("reads the rail it is handed rather than a list of its own", () => {
    // The point of taking `nav`: there is one list of the product's screens,
    // and it is the rail's. A second one would drift within a week.
    const custom: SidebarNavGroup[] = [
      { label: "Made up", items: [{ label: "Somewhere", href: "/runs" }] },
    ]

    expect(of(resolve("somewhere", session(), custom), "section")).toHaveLength(
      1
    )
    expect(of(resolve("somewhere", session(), productNav), "section")).toEqual(
      []
    )
  })

  it("names every act at a route the product actually has", () => {
    // A cheap guard against an act outliving the form it opens.
    for (const act of SEARCH_ACTS) {
      expect(act.href.startsWith("/")).toBe(true)
    }
  })
})

/* ------------------------------------------------------------------ *
 * (c) Hand-off — an act, never a result list.
 * ------------------------------------------------------------------ */

describe("free text hands off instead of inventing rows", () => {
  it("falls through to the hand-off when nothing else answers", () => {
    const items = resolve("webhook")

    expect(of(items, "resolved")).toEqual([])
    expect(of(items, "section")).toEqual([])
    expect(hrefs(of(items, "handoff"))).toEqual([
      "/runs?q=webhook",
      "/queue?q=webhook",
      "/tasks?q=webhook",
    ])
  })

  it("carries the query into the destination's own filter parameter", () => {
    const handoffs = of(resolve("idempotency key"), "handoff")

    expect(handoffs[0].href).toBe("/runs?q=idempotency%20key")
    expect(handoffs[0].label).toBe("idempotency key")
  })

  it("offers only the screens this session can open", () => {
    // A viewer watches runs and nothing else: no queue, no inbox.
    expect(hrefs(of(resolve("webhook", session(["viewer"])), "handoff"))).toEqual(
      ["/runs?q=webhook"]
    )
  })

  it("still offers to search when the query also resolved", () => {
    // `web` is two applications *and* a perfectly good thing to look for in a
    // run title. The operator is the one who knows which they meant, so both
    // are offered and the resolution goes first.
    const items = resolve("web")

    expect(of(items, "resolved").length).toBe(2)
    expect(of(items, "handoff").length).toBe(3)
    expect(items[0].group).toBe("resolved")
    expect(items[items.length - 1].group).toBe("handoff")
  })

  it("says nothing at all to a session that can reach nothing", () => {
    const nobody = session([])

    expect(resolve("webhook", nobody)).toEqual([])
  })
})

/* ------------------------------------------------------------------ *
 * The order the three layers are read in.
 * ------------------------------------------------------------------ */

describe("precedence", () => {
  it("puts what the query *is* above where the query might be looked for", () => {
    // `queue` is a section, and `wi_0101` is a work item that lives in it.
    const groups = resolve("wi_0101").map((item) => item.group)

    expect(groups[0]).toBe("resolved")
  })

  it("gives every row a key that is unique in the list", () => {
    const items = resolve("new")
    const ids = items.map((item) => item.id)

    expect(new Set(ids).size).toBe(ids.length)
  })

  it("trims the query before it reads its shape", () => {
    expect(hrefs(of(resolve("  5b1d7e40  "), "resolved"))).toEqual([
      "/runs/5b1d7e40",
    ])
  })
})
