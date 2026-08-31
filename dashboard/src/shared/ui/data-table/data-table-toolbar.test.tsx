import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { useState } from "react"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import {
  applyDataFilters,
  type DataColumn,
  type DataTableFilterValues,
} from "./data-table"
import { DataTableToolbar } from "./data-table-toolbar"

/* The rows are rendered as a plain list rather than through `DataTable`. The
   assertions here are about the filter row — which chips exist, what each one
   drops, what the count claims — and a virtualized body in a tree jsdom lays
   out at zero height is a surface those assertions can pass against while
   showing nothing. A `<ul>` is the honest reading surface for this file; the
   table has its own. */

interface Shard {
  id: string
  title: string
  app: string
  status: string
  profile: string
}

const rows: Shard[] = [
  {
    id: "r-1",
    title: "rotate the billing key",
    app: "plexor",
    status: "waiting",
    profile: "planner",
  },
  {
    id: "r-2",
    title: "drain the queue",
    app: "plexor",
    status: "running",
    profile: "builder",
  },
  {
    id: "r-3",
    title: "patch the auth path",
    app: "auth-svc",
    status: "waiting",
    profile: "builder",
  },
  {
    id: "r-4",
    title: "retire the old digest",
    app: "auth-svc",
    status: "running",
    profile: "planner",
  },
]

/* The status option's label is deliberately not its value: a chip has to show
   the words the operator picked, not the token the row is stored under. */
const statusColumn: DataColumn<Shard> = {
  accessorKey: "status",
  header: "status",
  meta: {
    filter: {
      kind: "select",
      placeholder: "all statuses",
      options: [
        { value: "waiting", label: "waiting on a human" },
        { value: "running", label: "running" },
      ],
    },
  },
}

const appColumn: DataColumn<Shard> = {
  accessorKey: "app",
  header: "app",
  meta: {
    filter: {
      kind: "select",
      placeholder: "all apps",
      options: [
        { value: "plexor", label: "plexor" },
        { value: "auth-svc", label: "auth-svc" },
      ],
    },
  },
}

const profileColumn: DataColumn<Shard> = {
  accessorKey: "profile",
  header: "profile",
  meta: {
    filter: {
      kind: "select",
      placeholder: "all profiles",
      options: [
        { value: "planner", label: "planner" },
        { value: "builder", label: "builder" },
      ],
    },
  },
}

/* Declared third on purpose: the row's search is the first *text* filter, not
   the first filter, and nothing about the column order decides it. */
const searchColumn: DataColumn<Shard> = {
  accessorKey: "title",
  header: "task",
  meta: {
    label: "task",
    filter: {
      kind: "text",
      placeholder: "search shard, task, app…",
      match: (shard, needle) =>
        `${shard.id} ${shard.title} ${shard.app}`
          .toLowerCase()
          .includes(needle.toLowerCase()),
    },
  },
}

const columns: DataColumn<Shard>[] = [
  statusColumn,
  appColumn,
  searchColumn,
  profileColumn,
]

function Board({
  columns: declared = columns,
  initial = {},
}: {
  columns?: DataColumn<Shard>[]
  initial?: DataTableFilterValues
}) {
  const [filters, setFilters] = useState<DataTableFilterValues>(initial)
  const shown = applyDataFilters(rows, filters, declared)

  return (
    <>
      <DataTableToolbar
        columns={declared}
        filters={filters}
        onFiltersChange={setFilters}
      />
      <ul data-test="rows">
        {shown.map((shard) => (
          <li key={shard.id}>{shard.id}</li>
        ))}
      </ul>
    </>
  )
}

const shownIds = () =>
  Array.from(
    document.querySelectorAll('[data-test="rows"] li'),
    (node) => node.textContent
  )

const chipNames = () =>
  Array.from(
    document.querySelectorAll('[data-test^="data-table-chip-"]'),
    (node) => node.textContent
  )

/** Opens the filter popover and hands back the dialog it put on the page. */
async function openSheet(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: /^Filters/ }))
  return screen.getByRole("dialog")
}

describe("DataTableToolbar", () => {
  it("shows the search field and the button, and no chip strip, when nothing is filtered", () => {
    render(<Board />)

    expect(screen.getByRole("searchbox", { name: "Filter by task" })).toBeTruthy()
    expect(screen.getByRole("button", { name: "Filters" })).toBeTruthy()
    // Not an empty strip — no strip. A row that reserves height for chips it
    // does not have is a row that lies about what is filtered.
    expect(document.querySelector('[data-test="data-table-chips"]')).toBeNull()
  })

  it("puts one chip on the row per active filter, and counts the same set on the button", () => {
    render(
      <Board
        initial={{ status: "waiting", app: "plexor", profile: "planner" }}
      />
    )

    // Each chip shows the words the option was picked by, not the stored token.
    expect(chipNames()).toEqual(["waiting on a human", "plexor", "planner"])
    // How many, and which — the same set said twice.
    expect(screen.getByRole("button", { name: "Filters, 3 active" })).toBeTruthy()
  })

  it("names each remove control by the filter it drops", () => {
    render(<Board initial={{ status: "waiting" }} />)

    // "×" is not a name. The control says which filter goes and what it holds.
    expect(
      screen.getByRole("button", {
        name: "Clear the status filter: waiting on a human",
      })
    ).toBeTruthy()
  })

  it("removes exactly its own filter when a chip is clicked", async () => {
    const user = userEvent.setup()
    render(<Board initial={{ status: "waiting", app: "plexor" }} />)

    expect(shownIds()).toEqual(["r-1"])

    await user.click(
      screen.getByRole("button", { name: "Clear the app filter: plexor" })
    )

    // The app filter is gone; the status filter is untouched.
    expect(chipNames()).toEqual(["waiting on a human"])
    expect(screen.getByRole("button", { name: "Filters, 1 active" })).toBeTruthy()
    expect(shownIds()).toEqual(["r-1", "r-3"])
  })

  it("returns the unfiltered list when everything is cleared", async () => {
    const user = userEvent.setup()
    render(
      <Board
        initial={{ status: "waiting", app: "plexor", title: "billing" }}
      />
    )

    expect(shownIds()).toEqual(["r-1"])

    const sheet = await openSheet(user)
    await user.click(within(sheet).getByRole("button", { name: "Clear all filters" }))
    // An open popover hides the rest of the page from assistive tech, and the
    // row is what is being asserted on — so close it the way an operator would.
    await user.keyboard("{Escape}")

    expect(document.querySelector('[data-test="data-table-chips"]')).toBeNull()
    expect(screen.getByRole("button", { name: "Filters" })).toBeTruthy()
    // The search goes with them: it is a filter, it is just not a chip.
    expect(
      screen.getByRole<HTMLInputElement>("searchbox", { name: "Filter by task" })
        .value
    ).toBe("")
    expect(shownIds()).toEqual(["r-1", "r-2", "r-3", "r-4"])
  })

  it("holds every declared filter in the popover when none is promoted", async () => {
    const user = userEvent.setup()
    const declared = [statusColumn, appColumn, profileColumn]
    render(<Board columns={declared} />)

    // No text filter declared, so no search field: the button is the row's
    // left edge and the popover carries the whole declared set.
    expect(screen.queryByRole("searchbox")).toBeNull()

    const sheet = await openSheet(user)
    expect(
      Array.from(
        sheet.querySelectorAll("[data-test^='data-table-filter-']"),
        (node) => node.getAttribute("data-test")
      )
    ).toEqual([
      "data-table-filter-status",
      "data-table-filter-app",
      "data-table-filter-profile",
    ])
  })

  it("keeps the promoted text filter out of the popover and off the chips", async () => {
    const user = userEvent.setup()
    render(<Board />)

    await user.type(
      screen.getByRole("searchbox", { name: "Filter by task" }),
      "billing"
    )

    // The search drives the declared filter — the same one `applyDataFilters`
    // evaluates, not a second search competing with it.
    expect(shownIds()).toEqual(["r-1"])
    // Its value is legible in the field it was typed into, so it earns no chip
    // and is not in the count.
    expect(document.querySelector('[data-test="data-table-chips"]')).toBeNull()
    expect(screen.getByRole("button", { name: "Filters" })).toBeTruthy()

    const sheet = await openSheet(user)
    expect(
      sheet.querySelector("[data-test='data-table-filter-title']")
    ).toBeNull()
    expect(
      sheet.querySelectorAll("[data-test^='data-table-filter-']")
    ).toHaveLength(3)
  })

  it("drops the button on a screen whose only filter is the search", () => {
    render(<Board columns={[searchColumn]} />)

    expect(screen.getByRole("searchbox", { name: "Filter by task" })).toBeTruthy()
    // Nothing to put behind it, so there is no button to open onto nothing.
    expect(screen.queryByRole("button", { name: /^Filters/ })).toBeNull()
  })

  it("writes a filter from the popover and says so on the row", async () => {
    const user = userEvent.setup()
    render(<Board />)

    const sheet = await openSheet(user)
    await user.click(
      sheet.querySelector<HTMLElement>("[data-test='data-table-filter-app']")!
    )
    await user.click(screen.getByRole("option", { name: "auth-svc" }))

    expect(chipNames()).toEqual(["auth-svc"])
    expect(shownIds()).toEqual(["r-3", "r-4"])
  })
})

/* The sheet's measure ------------------------------------------------------
   The filter popover came out ragged and cramped, and the cause was not the
   grid. An overlay is absolutely positioned, so a popover with no `inline-size`
   shrink-wraps its content — and a `1fr` track resolves against a *definite*
   inline size, so inside a shrink-to-fit box the fractional tracks have nothing
   to distribute and collapse to max-content. Two fields holding different words
   then come out different widths, which is exactly what was reported.

   Half of the fix is a rendered fact (the count reaches the popover, not just
   the grid) and half is a declaration (the popover turns that count into a
   width). jsdom can see the first directly. The second it cannot see at all —
   Vitest stubs a CSS Module into a bag of class names and lays nothing out — so
   it is read back off disk, the way `data-table.test.tsx` guards the pinned
   columns it also cannot see. A definite width on an overlay reads as redundant
   to anyone who has not hit this bug, which is precisely why it needs a guard
   rather than a comment. */

/** The popover element itself — the sheet is the dialog *inside* it. */
function sheetPopover(sheet: HTMLElement): HTMLElement {
  const popover = sheet.parentElement
  if (!popover) {
    throw new Error("the sheet is not inside a popover")
  }
  return popover
}

const SHEET = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "data-table-toolbar.module.css"),
  "utf8"
).replace(/\/\*[\s\S]*?\*\//g, "")

/** One declared value from the rule whose selector list names `selector`. */
function declared(selector: string, property: string): string | undefined {
  const block = /([^{}]+)\{([^{}]*)\}/g
  let match: RegExpExecArray | null
  while ((match = block.exec(SHEET)) !== null) {
    const named = (match[1] ?? "")
      .split(",")
      .map((part) => part.trim())
      .includes(selector)
    if (!named) {
      continue
    }
    const hit = new RegExp(`(?:^|;)\\s*${property}\\s*:([^;]+)`).exec(
      match[2] ?? ""
    )
    if (hit) {
      return (hit[1] ?? "").trim()
    }
  }
  return undefined
}

describe("the filter sheet's measure", () => {
  it("puts the field count on the popover, not only on the grid", async () => {
    const user = userEvent.setup()
    render(<Board />)

    // Three filters behind the button (status, app, profile — the search is
    // promoted out), capped at two to a line.
    expect(
      sheetPopover(await openSheet(user)).style.getPropertyValue("--sheet-cols")
    ).toBe("2")
  })

  it("asks for one column when there is only one filter to show", async () => {
    const user = userEvent.setup()
    render(<Board columns={[statusColumn, searchColumn]} />)

    // A sheet holding one filter must be one field wide, not a panel with a
    // lone select adrift in it — which is what a flat measure would give.
    expect(
      sheetPopover(await openSheet(user)).style.getPropertyValue("--sheet-cols")
    ).toBe("1")
  })

  it("turns that count into a definite width (stylesheet contract)", () => {
    // Definite, and derived from the same number the grid lays out against.
    // Without a width the popover shrink-wraps and every `1fr` below it stops
    // distributing anything.
    const measure = declared(".sheetPopover", "inline-size")
    expect(measure).toBeDefined()
    expect(measure).toContain("var(--sheet-cols")
  })

  it("lets the clamp beat the measure, so 320px stacks rather than overflows", () => {
    // `max-inline-size` wins over `inline-size` by construction. Below the
    // measure the grid's own `auto-fit` drops to one column inside the clamped
    // box instead of pushing two floors past its edge.
    expect(declared(".sheetPopover", "max-inline-size")).toBe(
      "calc(100vw - var(--s8))"
    )
    const tracks = declared(".fields", "grid-template-columns") ?? ""
    expect(tracks).toContain("auto-fit")
    expect(tracks).toContain("min(9.5rem, 100%)")
  })
})
