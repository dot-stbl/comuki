import { useState } from "react"
import { fireEvent, render } from "@testing-library/react"
import { beforeAll, describe, expect, it } from "vitest"

import type { ProjectRef, Session } from "@/shared/session"
import {
  DataTable,
  applyDataFilters,
  dataFilterSpecs,
  type DataTableSorting,
} from "@/shared/ui"

import { queueOrder } from "@/domains/queue/model/queue"
import type { QueueItem, Worker } from "@/domains/queue/model/types"

import { createQueueColumns, getQueueItemId } from "./queue-columns"
import { createWorkerColumns } from "./worker-columns"

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

const PROJECTS: ProjectRef[] = [
  { id: "p_one", key: "one", name: "Project one" },
  { id: "p_two", key: "two", name: "Project two" },
]

function item(
  id: string,
  profile: string,
  status: QueueItem["status"],
  ageSec: number,
  projectId = "p_one"
): QueueItem {
  return {
    id,
    runId: "8f3c2a91",
    projectId,
    profile,
    label: `${profile} step`,
    status,
    ageSec,
    claimedBy: status === "running" ? "wk_0001" : null,
    blockedOn: [],
  }
}

const ITEMS: QueueItem[] = [
  item("wi_a", "verifier", "queued", 2612),
  item("wi_b", "implementer", "queued", 8),
  item("wi_c", "verifier", "running", 400),
  item("wi_d", "implementer", "blocked", 13260, "p_two"),
  item("wi_e", "docs", "queued", 664, "p_two"),
]

const columns = createQueueColumns({ projects: PROJECTS })

describe("the profile filter", () => {
  it("is declared on the profile column, so the toolbar assembles itself", () => {
    const specs = dataFilterSpecs(columns)
    const profile = specs.find((spec) => spec.id === "profile")

    expect(profile).toBeDefined()
    expect(profile?.filter.kind).toBe("select")
  })

  it("offers the whole declared catalog, not just what happens to be queued", () => {
    // Profiles are a closed catalog living in the client's git. A profile with
    // nothing on it is a real answer — "nothing is queued here" — and dropping
    // it from the list would make that answer unaskable.
    const profile = dataFilterSpecs(columns).find((spec) => spec.id === "profile")
    const options =
      profile?.filter.kind === "select" ? profile.filter.options : []

    expect(options.map((option) => option.value)).toContain("planner")
    expect(options.map((option) => option.value)).toContain("verifier")
  })

  it("narrows the list to one profile, across every status", () => {
    const rows = applyDataFilters(ITEMS, { profile: "verifier" }, columns)

    expect(rows.map((row) => row.id)).toEqual(["wi_a", "wi_c"])
  })

  it("composes with the other filters rather than replacing them", () => {
    const rows = applyDataFilters(
      ITEMS,
      { profile: "implementer", projectId: "p_two" },
      columns
    )

    expect(rows.map((row) => row.id)).toEqual(["wi_d"])
  })

  it("means nothing on this profile, not nothing at all", () => {
    expect(applyDataFilters(ITEMS, { profile: "planner" }, columns)).toEqual([])
    expect(applyDataFilters(ITEMS, { profile: "" }, columns)).toHaveLength(5)
  })
})

/** The table as the screen assembles it, minus the one column that needs a router. */
function QueueList({ data }: { data: QueueItem[] }) {
  const [sorting, setSorting] = useState<DataTableSorting>([])

  return (
    <DataTable
      columns={columns}
      data={data}
      getRowId={getQueueItemId}
      density="compact"
      sorting={sorting}
      onSortingChange={setSorting}
      /* The run column renders a router `Link` and there is no router here.
         Hiding it keeps this test on the age column instead of dragging a
         whole route tree in to look at a duration. */
      columnVisibility={{ runId: false }}
    />
  )
}

function ages(): string[] {
  return Array.from(document.querySelectorAll('[data-test="age-meter"]')).map(
    (node) => node.textContent ?? ""
  )
}

function heats(): (string | null)[] {
  return Array.from(document.querySelectorAll('[data-test="age-meter"]')).map(
    (node) => node.getAttribute("data-heat")
  )
}

describe("age ordering", () => {
  it("opens on the longest unclaimed wait, not on the oldest row", () => {
    render(<QueueList data={queueOrder(ITEMS)} />)

    // 43:32 queued beats 3:41:00 blocked, because only one of them is a fault.
    expect(ages()[0]).toBe("43:32")
    expect(ages()).toEqual(["43:32", "11:04", "00:08", "06:40", "221:00"])
  })

  it("sorts by the number, not by how the duration spells", () => {
    render(<QueueList data={queueOrder(ITEMS)} />)

    // Ascending first: 8s before 6:40 before 11:04 — a string comparison would
    // have put "00:08" first and then "06:40" after "11:04" is 221:00.
    fireEvent.click(document.querySelector('[data-test="data-table-sort-ageSec"]')!)
    expect(ages()).toEqual(["00:08", "06:40", "11:04", "43:32", "221:00"])

    fireEvent.click(document.querySelector('[data-test="data-table-sort-ageSec"]')!)
    expect(ages()).toEqual(["221:00", "43:32", "11:04", "06:40", "00:08"])
  })

  it("marks the wait only where waiting means something", () => {
    render(<QueueList data={queueOrder(ITEMS)} />)

    // queued 43:32 · queued 11:04 · queued 00:08 · running · blocked
    expect(heats()).toEqual(["stalled", "stalled", "fresh", "none", "none"])
  })
})

/* ------------------------------------------------------------------ *
 * The promoted text filter, and the project key inside it
 *
 * Both halves of the screen are here rather than in two files, because this is
 * one decision made twice: a hand-off that cannot be received lands the
 * operator on an empty screen, which is the contract at the top of
 * `app/search/shapes.ts`. The project detail page sends its queue off as
 * `/queue?q=<slug>`, and `?q=` and `?w=` are these two boxes.
 * ------------------------------------------------------------------ */

const SESSION: Session = {
  user: {
    id: "u_test",
    name: "Test User",
    email: "test@comuki.local",
    platformRoles: ["platform-admin"],
    projectRoles: {},
  },
  projects: PROJECTS,
}

/** Handles and digests deliberately free of either project handle, so a hit
 *  below can only have come from the key itself. */
const WORKERS: Worker[] = [
  {
    id: "wk_aaaa",
    projectId: "p_one",
    profile: "implementer",
    state: "busy",
    itemId: "wi_a",
    provider: "kubernetes",
    handle: "k8s/cluster-a/worker-implementer-aaaa",
    heartbeatAgeSec: 3,
    leaseSec: 214,
    upSec: 1840,
    digest: "sha256:9c41ab",
  },
  {
    id: "wk_bbbb",
    projectId: "p_two",
    profile: "docs",
    state: "idle",
    itemId: null,
    provider: "docker",
    handle: "docker/cluster-b/bbbb4411",
    heartbeatAgeSec: 1,
    leaseSec: null,
    upSec: 5400,
    digest: "sha256:9c41ab",
  },
]

const workerColumns = createWorkerColumns({
  projects: PROJECTS,
  itemsById: new Map(),
  session: SESSION,
  drainingId: null,
  stoppingId: null,
  onDrain: () => {},
  onForceStop: () => {},
})

describe("the queue's text box answers to a project key", () => {
  it("finds the project's items, so `/queue?q=<slug>` is not an empty screen", () => {
    expect(
      applyDataFilters(ITEMS, { label: "two" }, columns).map((row) => row.id)
    ).toEqual(["wi_d", "wi_e"])
    expect(
      applyDataFilters(ITEMS, { label: "one" }, columns).map((row) => row.id)
    ).toEqual(["wi_a", "wi_b", "wi_c"])
  })

  it("still answers to everything it answered to before", () => {
    // The key was added to the haystack, not swapped in for what was there.
    expect(
      applyDataFilters(ITEMS, { label: "wi_c" }, columns).map((row) => row.id)
    ).toEqual(["wi_c"])
    expect(
      applyDataFilters(ITEMS, { label: "docs step" }, columns).map(
        (row) => row.id
      )
    ).toEqual(["wi_e"])
  })

  it("does not advertise the key in its placeholder", () => {
    // Project has a column of its own and a select of its own. A third way in
    // would teach the operator to type where they should be picking.
    const text = dataFilterSpecs(columns).find(
      (spec) => spec.filter.kind === "text"
    )
    const placeholder =
      text?.filter.kind === "text" ? (text.filter.placeholder ?? "") : ""

    expect(placeholder).toBe("filter item, run, step…")
    expect(placeholder).not.toContain("project")
  })
})

describe("the pool's text box answers to a project key too", () => {
  it("finds the project's containers", () => {
    expect(
      applyDataFilters(WORKERS, { id: "one" }, workerColumns).map(
        (row) => row.id
      )
    ).toEqual(["wk_aaaa"])
    expect(
      applyDataFilters(WORKERS, { id: "two" }, workerColumns).map(
        (row) => row.id
      )
    ).toEqual(["wk_bbbb"])
  })

  it("keeps the three identifiers a container is actually found by", () => {
    expect(
      applyDataFilters(WORKERS, { id: "wk_bbbb" }, workerColumns)
    ).toHaveLength(1)
    expect(
      applyDataFilters(WORKERS, { id: "cluster-a" }, workerColumns)
    ).toHaveLength(1)
    // The digest is on both, and that is the whole reason it is in the box:
    // "which containers are on this image" is one question with many answers.
    expect(
      applyDataFilters(WORKERS, { id: "sha256:9c41ab" }, workerColumns)
    ).toHaveLength(2)
  })
})
