/* The app project types itself against the browser (`types: ["vite/client"]`),
   which is right — nothing that ships may reach for a Node built-in. This file
   does not ship: it reads its own stylesheet off disk, so it asks for the Node
   types here rather than widening the whole project's. */
/// <reference types="node" />
import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { useState } from "react"
import { fireEvent, render } from "@testing-library/react"
import { beforeAll, describe, expect, it } from "vitest"

import {
  DataTable,
  rankSort,
  type DataColumn,
  type DataTableColumnSizing,
  type DataTableSorting,
} from "./data-table"

/* jsdom implements neither, and the virtualizer needs both: a ResizeObserver
   to watch the scroll port, and a scroll port with a height to decide how many
   rows are worth rendering. Without them the body renders nothing and the
   assertions below would pass for the wrong reason. */
const PORT_HEIGHT = 320

beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
  // The virtualizer sizes the port from `offsetHeight`, which jsdom pins at 0.
  Object.defineProperty(HTMLElement.prototype, "offsetHeight", {
    configurable: true,
    value: PORT_HEIGHT,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 960,
  })
  /* jsdom has PointerEvent but no pointer capture, and the resize drag calls
     it. Nothing here asserts on capture — it is what keeps a real pointer
     delivering moves after the column has narrowed out from under it — so a
     no-op is the honest stub. */
  if (!HTMLElement.prototype.setPointerCapture) {
    HTMLElement.prototype.setPointerCapture = () => {}
    HTMLElement.prototype.releasePointerCapture = () => {}
    HTMLElement.prototype.hasPointerCapture = () => false
  }
})

/* What these tests can and cannot see. jsdom lays nothing out, so every
   `getBoundingClientRect` is zero and the component falls back to the model
   width — which is exactly the path a drag takes in a tree with no layout, and
   why the numbers below are the model's, not the browser's. That makes these
   tests a check on the width *source* — one number per column, written into
   the same style on the head and on every body cell, and summed into the
   table's own inline size — and not a check on the layout it produces. The
   layout was traced by hand; see the notes on `.table` in the stylesheet. */

interface Shard {
  id: string
  status: string
  cost: number
}

/**
 * Deliberately not alphabetical: alphabetically `success` sits before
 * `waiting`, and the whole point of a rank is that the domain disagrees.
 */
const RANK: Record<string, number> = {
  escalated: 0,
  failed: 1,
  waiting: 2,
  running: 3,
  queued: 4,
  success: 5,
}

const columns: DataColumn<Shard>[] = [
  {
    accessorKey: "status",
    header: "status",
    sortFn: rankSort(RANK),
  },
  {
    accessorKey: "cost",
    header: "cost",
    meta: { numeric: true },
  },
  {
    id: "actions",
    header: "actions",
    enableSorting: false,
    cell: () => "retry",
  },
]

const SHARDS: Shard[] = [
  { id: "a", status: "success", cost: 9 },
  { id: "b", status: "waiting", cost: 10 },
  { id: "c", status: "escalated", cost: 2 },
]

function bulk(count: number): Shard[] {
  const statuses = Object.keys(RANK)
  return Array.from({ length: count }, (_, index) => ({
    id: `shard-${index}`,
    status: statuses[index % statuses.length] ?? "queued",
    cost: (index * 37) % 1000,
  }))
}

function Harness({ data }: { data: Shard[] }) {
  // The screen owns the slice — exactly how a page holds it.
  const [sorting, setSorting] = useState<DataTableSorting>([])
  return (
    <DataTable
      columns={columns}
      data={data}
      getRowId={(row) => row.id}
      sorting={sorting}
      onSortingChange={setSorting}
    />
  )
}

function header(container: HTMLElement, name: string): HTMLTableCellElement {
  const cell = [...container.querySelectorAll("th")].find(
    (node) => node.textContent?.trim() === name
  )
  if (!cell) {
    throw new Error(`no ${name} header`)
  }
  return cell
}

function firstCells(container: HTMLElement): string[] {
  return [...container.querySelectorAll('[data-test="data-table-row"]')].map(
    (row) => row.querySelector("td")?.textContent?.trim() ?? ""
  )
}

function rowCount(container: HTMLElement): number {
  return container.querySelectorAll('[data-test="data-table-row"]').length
}

describe("DataTable sorting", () => {
  it("puts a button in sortable heads and leaves the rest plain", () => {
    const { container } = render(<Harness data={SHARDS} />)

    expect(header(container, "status").querySelector("button")).not.toBeNull()
    expect(header(container, "cost").querySelector("button")).not.toBeNull()
    expect(header(container, "actions").querySelector("button")).toBeNull()
    expect(header(container, "actions").getAttribute("aria-sort")).toBeNull()
  })

  it("cycles aria-sort on the th: ascending, descending, none", () => {
    const { container } = render(<Harness data={SHARDS} />)
    const th = header(container, "status")
    const button = th.querySelector("button")

    expect(th.getAttribute("aria-sort")).toBe("none")

    fireEvent.click(button!)
    expect(th.getAttribute("aria-sort")).toBe("ascending")

    fireEvent.click(th.querySelector("button")!)
    expect(th.getAttribute("aria-sort")).toBe("descending")

    fireEvent.click(th.querySelector("button")!)
    expect(th.getAttribute("aria-sort")).toBe("none")
  })

  it("orders the status column by rank, not by spelling", () => {
    const { container } = render(<Harness data={SHARDS} />)

    fireEvent.click(header(container, "status").querySelector("button")!)
    expect(firstCells(container)).toEqual(["escalated", "waiting", "success"])

    fireEvent.click(header(container, "status").querySelector("button")!)
    expect(firstCells(container)).toEqual(["success", "waiting", "escalated"])
  })

  it("compares a numeric column as numbers", () => {
    const { container } = render(<Harness data={SHARDS} />)

    fireEvent.click(header(container, "cost").querySelector("button")!)
    // Lexically "10" precedes "2"; numerically it does not.
    expect(firstCells(container)).toEqual(["escalated", "success", "waiting"])
  })

  it("keeps the rendered row count bounded after a sort", () => {
    // A port this deep holds ~12 rows plus overscan either side; the number
    // must not track the data length, before or after a sort.
    const BOUND = 64
    const rows = bulk(500)
    const { container } = render(<Harness data={rows} />)

    const idle = rowCount(container)
    expect(idle).toBeGreaterThan(0)
    expect(idle).toBeLessThanOrEqual(BOUND)

    fireEvent.click(header(container, "cost").querySelector("button")!)

    expect(rowCount(container)).toBeLessThanOrEqual(BOUND)
    // Sorting reorders the row model the virtualizer measures off; it does not
    // hand the body every row.
    expect(firstCells(container)[0]).toBe("escalated")
  })
})

/* Column sizing and pinning ---------------------------------------------- */

/** Declared widths, so the assertions below can name the numbers they expect. */
const STATUS_W = 120
const COST_W = 96
const ACTIONS_W = 80
/** Mirrors the component's own floor and arrow step. */
const MIN_W = 56
const STEP = 8

const sizedColumns: DataColumn<Shard>[] = [
  {
    accessorKey: "status",
    header: "status",
    sortFn: rankSort(RANK),
    meta: { width: STATUS_W, pinned: true },
  },
  {
    accessorKey: "cost",
    header: "cost",
    meta: { width: COST_W, numeric: true },
  },
  {
    id: "actions",
    header: "actions",
    enableSorting: false,
    cell: () => "retry",
    meta: { width: ACTIONS_W, label: "actions", resizable: false },
  },
]

function SizedHarness({
  initial = {},
  onChange,
}: {
  initial?: DataTableColumnSizing
  onChange?: (next: DataTableColumnSizing) => void
}) {
  // The screen owns the slice, exactly how a page holds it.
  const [sizing, setSizing] = useState<DataTableColumnSizing>(initial)
  return (
    <DataTable
      columns={sizedColumns}
      data={SHARDS}
      getRowId={(row) => row.id}
      columnSizing={sizing}
      onColumnSizingChange={(next) => {
        setSizing(next)
        onChange?.(next)
      }}
    />
  )
}

function grip(container: HTMLElement, columnId: string): HTMLButtonElement {
  const node = container.querySelector<HTMLButtonElement>(
    `[data-test="data-table-resize-${columnId}"]`
  )
  if (!node) {
    throw new Error(`no grip for ${columnId}`)
  }
  return node
}

/** Every rendered track for a column: its head, then one cell per row. */
function tracks(container: HTMLElement, index: number): HTMLElement[] {
  const heads = [...container.querySelectorAll("thead tr")].map(
    (row) => row.children[index] as HTMLElement
  )
  const cells = [
    ...container.querySelectorAll('[data-test="data-table-row"]'),
  ].map((row) => row.children[index] as HTMLElement)
  return [...heads, ...cells]
}

function scrollPort(container: HTMLElement): HTMLElement {
  const port = container.querySelector<HTMLElement>(
    '[data-test="data-table"] > div'
  )
  if (!port) {
    throw new Error("no scroll port")
  }
  return port
}

describe("DataTable column sizing", () => {
  it("puts a named grip on resizable heads and nowhere else", () => {
    const { container } = render(<SizedHarness />)

    expect(grip(container, "status").getAttribute("aria-label")).toBe(
      "Resize status column"
    )
    expect(grip(container, "cost").getAttribute("aria-label")).toBe(
      "Resize cost column"
    )
    // `meta.resizable: false` opts a column out entirely.
    expect(
      container.querySelector('[data-test="data-table-resize-actions"]')
    ).toBeNull()
  })

  it("leaves the head plain when the screen holds no sizing state", () => {
    const { container } = render(
      <DataTable
        columns={sizedColumns}
        data={SHARDS}
        getRowId={(row) => row.id}
      />
    )

    expect(
      container.querySelectorAll('[data-test^="data-table-resize-"]')
    ).toHaveLength(0)
  })

  it("steps a column with the arrow keys, coarser with shift", () => {
    let latest: DataTableColumnSizing = {}
    const { container } = render(
      <SizedHarness
        onChange={(next) => {
          latest = next
        }}
      />
    )

    fireEvent.keyDown(grip(container, "cost"), { key: "ArrowRight" })
    expect(latest.cost).toBe(COST_W + STEP)

    fireEvent.keyDown(grip(container, "cost"), { key: "ArrowRight" })
    expect(latest.cost).toBe(COST_W + STEP * 2)

    fireEvent.keyDown(grip(container, "cost"), { key: "ArrowLeft" })
    expect(latest.cost).toBe(COST_W + STEP)

    fireEvent.keyDown(grip(container, "cost"), {
      key: "ArrowRight",
      shiftKey: true,
    })
    expect(latest.cost).toBe(COST_W + STEP + 32)
    // One column at a time: nothing else moved.
    expect(Object.keys(latest)).toEqual(["cost"])
  })

  it("refuses to shrink a column to nothing", () => {
    let latest: DataTableColumnSizing = {}
    const { container } = render(
      <SizedHarness
        onChange={(next) => {
          latest = next
        }}
      />
    )

    for (let press = 0; press < 40; press += 1) {
      fireEvent.keyDown(grip(container, "cost"), {
        key: "ArrowLeft",
        shiftKey: true,
      })
    }

    expect(latest.cost).toBe(MIN_W)
  })

  it("returns a column to its declared width on reset", () => {
    let latest: DataTableColumnSizing = {}
    const { container } = render(
      <SizedHarness
        initial={{ cost: 320 }}
        onChange={(next) => {
          latest = next
        }}
      />
    )

    expect(tracks(container, 1)[0].style.flex).toContain("320px")

    fireEvent.doubleClick(grip(container, "cost"))

    // Reset is a delete, not a write: the column falls back to `meta.width`.
    expect(latest.cost).toBeUndefined()
    expect(tracks(container, 1)[0].style.flex).toContain(`${COST_W}px`)

    // Enter reaches the same reset the pointer does.
    fireEvent.keyDown(grip(container, "cost"), { key: "ArrowRight" })
    expect(latest.cost).toBe(COST_W + STEP)
    fireEvent.keyDown(grip(container, "cost"), { key: "Enter" })
    expect(latest.cost).toBeUndefined()
  })

  it("drags a column with the pointer", () => {
    let latest: DataTableColumnSizing = {}
    const { container } = render(
      <SizedHarness
        onChange={(next) => {
          latest = next
        }}
      />
    )
    const handle = grip(container, "cost")

    fireEvent.pointerDown(handle, { button: 0, pointerId: 1, clientX: 200 })
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 260 })
    expect(latest.cost).toBe(COST_W + 60)

    // Absolute, not incremental: a second move is measured from the grab, so a
    // dropped frame cannot make the track drift away from the pointer.
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 230 })
    expect(latest.cost).toBe(COST_W + 30)

    fireEvent.pointerUp(handle, { pointerId: 1, clientX: 230 })
    // The gesture is over; a stray move is not the drag continuing.
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 400 })
    expect(latest.cost).toBe(COST_W + 30)
  })

  it("hands the head, every body cell and the table one width", () => {
    const { container } = render(<SizedHarness initial={{ cost: 240 }} />)

    for (const track of tracks(container, 1)) {
      expect(track.style.flex).toBe("0 0 240px")
    }
    // The table's own inline size is the sum of exactly those tracks, so the
    // head and the body cannot end in different places.
    expect(scrollPort(container).style.getPropertyValue("--dt-total-w")).toBe(
      `${STATUS_W + 240 + ACTIONS_W}px`
    )
  })

  it("keeps a resized column out of the flexible pool", () => {
    // A column with no declared width grows into the port's slack…
    const { container, rerender } = render(
      <DataTable
        columns={columns}
        data={SHARDS}
        getRowId={(row) => row.id}
        columnSizing={{}}
        onColumnSizingChange={() => {}}
      />
    )
    expect(tracks(container, 0)[0].style.flex).toBe("1 0 96px")

    // …until it has been sized, at which point it is a width like any other.
    rerender(
      <DataTable
        columns={columns}
        data={SHARDS}
        getRowId={(row) => row.id}
        columnSizing={{ status: 200 }}
        onColumnSizingChange={() => {}}
      />
    )
    expect(tracks(container, 0)[0].style.flex).toBe("0 0 200px")
  })
})

describe("DataTable pinned columns", () => {
  it("pins the declared column in the head and in every row alike", () => {
    const { container } = render(<SizedHarness />)

    for (const track of tracks(container, 0)) {
      expect(track.getAttribute("data-pinned")).toBe("")
      expect(track.style.insetInlineStart).toBe("0px")
    }
    for (const track of tracks(container, 1)) {
      expect(track.getAttribute("data-pinned")).toBeNull()
      expect(track.style.insetInlineStart).toBe("")
    }
  })

  it("parks a second pinned column beside the first, not on top of it", () => {
    const twoPinned = sizedColumns.map((column, index) =>
      index === 1 ? { ...column, meta: { ...column.meta, pinned: true } } : column
    )
    const { container } = render(
      <DataTable columns={twoPinned} data={SHARDS} getRowId={(row) => row.id} />
    )

    // The offset is the sum of the pinned tracks ahead of it, so widening the
    // first moves the second.
    expect(tracks(container, 1)[0].style.insetInlineStart).toBe(`${STATUS_W}px`)
  })

  it("carries the checkbox gutter along with the pin", () => {
    const { container } = render(
      <DataTable
        columns={sizedColumns}
        data={SHARDS}
        getRowId={(row) => row.id}
        selection={{ value: {}, onChange: () => {}, noun: "shard" }}
      />
    )

    // A row you can read and cannot select is not a row you can act on, so the
    // gutter pins too — and `status` parks after it rather than over it.
    expect(tracks(container, 0)[0].getAttribute("data-pinned")).toBe("")
    expect(tracks(container, 1)[0].style.insetInlineStart).toBe("28px")
  })

  it("gives the checkbox track a gutter that cannot clip its own box", () => {
    const { container } = render(
      <DataTable
        columns={sizedColumns}
        data={SHARDS}
        getRowId={(row) => row.id}
        selection={{ value: {}, onChange: () => {}, noun: "shard" }}
      />
    )

    // The width the next pinned column offsets itself by is the width the
    // checkbox has to fit inside, so the track opts out of the text gutter.
    // Widening the cell padding without this is how a fixed column starts
    // clipping the only control in it.
    for (const track of tracks(container, 0)) {
      expect(track.className).toContain("gutter")
      expect(track.style.flex).toBe("0 0 28px")
    }
    expect(tracks(container, 1)[0].className).not.toContain("gutter")
  })

  it("shows the seam only once the port has actually moved", () => {
    const { container } = render(<SizedHarness />)
    const frame = container.querySelector<HTMLElement>(
      '[data-test="data-table"]'
    )!
    const port = scrollPort(container)

    // The flag sits on the frame, because the frame is what draws the seam.
    // It rode on the port while the seam was a shadow on cells inside it.
    expect(frame.getAttribute("data-scrolled-x")).toBeNull()

    port.scrollLeft = 120
    fireEvent.scroll(port)
    expect(frame.getAttribute("data-scrolled-x")).toBe("")

    port.scrollLeft = 0
    fireEvent.scroll(port)
    expect(frame.getAttribute("data-scrolled-x")).toBeNull()
  })

  it("draws one seam for the frame, not one edge per row", () => {
    // The defect this replaced: `.pinnedEdge` was a per-cell class, so a
    // scrolled table cast a shadow per visible row — thirty-odd blurred boxes
    // stacked on top of each other, bleeding onto their neighbours and onto the
    // content beside them. A seam is one continuous edge or it is a smear.
    const { container } = render(
      <DataTable
        columns={sizedColumns}
        data={bulk(200)}
        getRowId={(row) => row.id}
      />
    )

    // Many rows on purpose: the old device scaled with the row count, so a
    // three-row fixture would have hidden it.
    expect(
      container.querySelectorAll('[data-test="data-table-row"]').length
    ).toBeGreaterThan(3)
    expect(
      container.querySelectorAll('[data-test="data-table-seam"]')
    ).toHaveLength(1)
  })

  it("puts the seam where the pinned block actually ends", () => {
    const twoPinned = sizedColumns.map((column, index) =>
      index === 1 ? { ...column, meta: { ...column.meta, pinned: true } } : column
    )
    const { container } = render(
      <DataTable columns={twoPinned} data={SHARDS} getRowId={(row) => row.id} />
    )
    const frame = container.querySelector<HTMLElement>(
      '[data-test="data-table"]'
    )!

    // The offset the seam reads and the offsets the cells park at come off one
    // sum, so widening a pinned column moves both or neither.
    expect(frame.style.getPropertyValue("--dt-pinned-w")).toBe(
      `${STATUS_W + COST_W}px`
    )
  })

  it("draws no seam at all when nothing is held", () => {
    const unpinned = sizedColumns.map((column) => ({
      ...column,
      meta: { ...column.meta, pinned: false },
    }))
    const { container } = render(
      <DataTable columns={unpinned} data={SHARDS} getRowId={(row) => row.id} />
    )

    // A seam sitting on the frame's own left edge is a hairline nobody asked
    // for, so it is not rendered rather than rendered at zero.
    expect(container.querySelector("[data-test='data-table-seam']")).toBeNull()
  })

  it("publishes one row height, and the same one the virtualizer offsets by", () => {
    // The painted rule and the computed offset come off a single constant, so
    // retuning density can never leave rows overlapping or gapping. A test can
    // only see the published half — but the published half is the constant.
    const compact = render(<SizedHarness />)
    const height = scrollPort(compact.container).style.getPropertyValue(
      "--dt-row-h"
    )
    expect(height).toMatch(/^\d+px$/)

    const comfortable = render(
      <DataTable
        columns={sizedColumns}
        data={SHARDS}
        getRowId={(row) => row.id}
        density="comfortable"
      />
    )
    const roomier = scrollPort(comfortable.container).style.getPropertyValue(
      "--dt-row-h"
    )
    expect(Number.parseInt(roomier, 10)).toBeGreaterThan(
      Number.parseInt(height, 10)
    )
  })
})

/* The pin's floor --------------------------------------------------------
   A pin is a promise that a column stays while the rest scrolls past it, and
   the promise is only keepable while there is a rest left to see. The declared
   pinned runs on the shipped screens are 224px (runs: status + id) and 316px
   (sources: state + name, and 344px the day a screen turns selection on), so
   on a narrow board the pinned block can be wider than the whole scroll port.
   When it is, the pinned cells cover the port end to end and scrolling
   sideways moves columns that are permanently underneath them: the reading is
   not degraded, it is unreachable, and no gesture recovers it.

   Below the floor the pins therefore come off and the table becomes an
   ordinary sideways-scrolling grid where every column can be reached. This is
   the one piece of the layout the component decides for itself, so it is also
   the one piece a rendered test can see — by handing it a port width, which is
   the only measurement jsdom will let us fake. */

/** Runs `body` with every element reporting `width` as its `clientWidth`. */
function withPortWidth(width: number, body: () => void): void {
  const original = Object.getOwnPropertyDescriptor(
    HTMLElement.prototype,
    "clientWidth"
  )
  Object.defineProperty(HTMLElement.prototype, "clientWidth", {
    configurable: true,
    get: () => width,
  })
  try {
    body()
  } finally {
    if (original) {
      Object.defineProperty(HTMLElement.prototype, "clientWidth", original)
    } else {
      // @ts-expect-error — removing a property jsdom does not define itself.
      delete HTMLElement.prototype.clientWidth
    }
  }
}

describe("DataTable pin floor", () => {
  it("holds the pin while the port has room for it and a column besides", () => {
    withPortWidth(STATUS_W + MIN_W + 40, () => {
      const { container } = render(<SizedHarness />)
      expect(tracks(container, 0)[0].getAttribute("data-pinned")).toBe("")
    })
  })

  it("drops the pin when the pinned run would swallow the port", () => {
    // One pixel short of holding the pinned track plus the narrowest column a
    // head label survives in. Every column is still rendered and still sized —
    // what goes is the sticky flag and the offset that parks it, so the port
    // scrolls the whole grid instead of scrolling it under a cover.
    withPortWidth(STATUS_W + MIN_W - 1, () => {
      const { container } = render(<SizedHarness />)
      for (const track of tracks(container, 0)) {
        expect(track.getAttribute("data-pinned")).toBeNull()
        expect(track.style.insetInlineStart).toBe("")
      }
      expect(tracks(container, 0)[0].style.flex).toBe(`0 0 ${STATUS_W}px`)
    })
  })

  it("keeps the pin when nothing has measured the port yet", () => {
    // 0 is "not measured" — the first paint, and every tree with no layout at
    // all. A declared pin must never be lost because nobody has measured; the
    // wrong reading here would be a table that silently unpins itself in every
    // environment that does not run a ResizeObserver.
    withPortWidth(0, () => {
      const { container } = render(<SizedHarness />)
      expect(tracks(container, 0)[0].getAttribute("data-pinned")).toBe("")
    })
  })
})

/* The pin's paint order --------------------------------------------------
   The duty engineer's report was "sometimes the first columns scroll along
   with the rows", and the cause was never the offset — the tests above prove
   that number. It was paint order. `.head` and `.pinned` both declared
   `z-index: 1`, and nothing between the scroll port and a cell opens a
   stacking context (`.table` is a static grid, `.body` was `position:
   relative; z-index: auto`, `.row` is `position: absolute; z-index: auto`), so
   the two competed in one context at equal weight and document order settled
   it: the body comes after the head, so the pinned cells painted *over* the
   sticky head. Every other column vanished under the header band on the way
   up; the pinned ones kept going, visibly, through it.

   jsdom lays nothing out and Vitest does not process the stylesheet, so paint
   order is not observable from a rendered tree. What is observable is the
   stylesheet's own contract, so this reads it back. It is deliberately a small
   set: the layer order, the isolation that makes the layer order local, and
   the ancestor properties that silently re-anchor a sticky cell to something
   that is not the scroll port — which is the *other* way this bug comes back,
   and the one the file's comments have always claimed and never checked. */

/* Read off disk rather than imported: Vitest stubs a CSS Module into a bag of
   class names, and `?raw` goes through the same stub. Split from `import.meta.url`
   in two steps on purpose — `new URL("…", import.meta.url)` is a pattern Vite
   rewrites into an asset reference, which is not a path to anything here. */
const SHEET = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "data-table.module.css"),
  "utf8"
).replace(/\/\*[\s\S]*?\*\//g, "")

/** The declaration bodies of every rule whose selector list names `selector`. */
function rules(selector: string): string[] {
  const found: string[] = []
  const block = /([^{}]+)\{([^{}]*)\}/g
  let match: RegExpExecArray | null
  while ((match = block.exec(SHEET)) !== null) {
    const named = (match[1] ?? "")
      .split(",")
      .map((part) => part.trim())
      .includes(selector)
    if (named) {
      found.push(match[2] ?? "")
    }
  }
  return found
}

/** One declared value, or `undefined` when the rule never sets it. */
function declared(selector: string, property: string): string | undefined {
  for (const body of rules(selector)) {
    const hit = new RegExp(`(?:^|;)\\s*${property}\\s*:([^;]+)`).exec(body)
    if (hit) {
      return (hit[1] ?? "").trim()
    }
  }
  return undefined
}

/** A `z-index` resolved through the named scale `.root` publishes. */
function layer(selector: string): number {
  const value = declared(selector, "z-index") ?? ""
  const token = /var\((--[\w-]+)\)/.exec(value)?.[1]
  return Number(token ? declared(".root", token) : value)
}

/** Anything on an ancestor that re-anchors or re-parents a sticky cell. */
const ANCHOR_BREAKERS = [
  "overflow",
  "overflow-x",
  "overflow-y",
  "transform",
  "filter",
  "backdrop-filter",
  "perspective",
  "contain",
  "container-type",
  "content-visibility",
  "will-change",
]

describe("DataTable pin (stylesheet contract)", () => {
  it("keeps the sticky head above every pinned cell", () => {
    expect(layer(".head")).toBeGreaterThan(layer(".pinned"))
  })

  it("keeps the resize grip below the track that is holding", () => {
    expect(layer(".grip")).toBeLessThan(layer(".pinned"))
  })

  it("isolates the body, so no cell can rank itself against the head", () => {
    // The layer numbers above are only local if this holds: without it a cell
    // is competing in the table's stacking context, where the head is.
    expect(declared(".body", "isolation")).toBe("isolate")
  })

  it("leaves the scroll port the only sticky anchor above a cell", () => {
    expect(declared(".scroll", "overflow")).toBe("auto")

    for (const selector of [".table", ".head", ".headRow", ".body", ".row"]) {
      for (const property of ANCHOR_BREAKERS) {
        expect({ selector, property, value: declared(selector, property) })
          .toEqual({ selector, property, value: undefined })
      }
    }
  })

  it("keeps every pinned track sticky and opaque", () => {
    // A transparent sticky cell shows the row sliding under it, which reads as
    // a column that failed to pin even when it did.
    expect(declared(".pinned", "position")).toBe("sticky")
    expect(declared(".pinned", "background")).toBeDefined()
    expect(declared(".head .pinned", "background")).toBeDefined()
    expect(declared(".row:hover .pinned", "background")).toBeDefined()
  })

  it("bounds the table on all four sides", () => {
    // The frame, not two hairlines: a surface that ends in mid-air on the
    // inline axis reads as clipped rather than as scrolled.
    expect(declared(".root", "border")).toContain("var(--rule-strong)")
    // The frame carries the surface step of the corner scale. It used to
    // assert the opposite — the No-Card Rule was read as "no radius" — and a
    // square data surface under rounded buttons is what made the product read
    // as half-rounded. The rule is about a fill and a shadow, which are still
    // absent; the corner was never the thing that made a card a card.
    expect(declared(".root", "border-radius")).toBe("var(--r-lg)")
    // The depth cap is the frame's, so the bottom edge lands inside it.
    expect(declared(".root", "max-block-size")).toBe("var(--h-table-body)")
  })

  it("cuts the frame's corners with clip, never with a scroll container", () => {
    // A rounded frame has to clip its rows or the corners paint through them.
    // `overflow: hidden` would do it — and would also make `.root` a scroll
    // container, which is what `position: sticky` resolves against. Every
    // pinned cell would re-anchor from `.scroll` up to here and stop holding,
    // which is precisely the pinned-column bug this suite exists to prevent.
    // `clip` cuts without opening a scroll port, so it is the only value that
    // may ever appear here.
    expect(declared(".root", "overflow")).toBe("clip")
    for (const property of ["overflow-x", "overflow-y"]) {
      expect({ property, value: declared(".root", property) })
        .toEqual({ property, value: undefined })
    }
  })

  it("draws the seam once, against the frame, above everything it crosses", () => {
    // The seam was a `box-shadow` on `.pinnedEdge` — a per-cell class — so a
    // scrolled table drew one blurred box per row and they bled into each
    // other. A `box-shadow` blurs on the block axis too, so no spread value
    // was ever going to make N boxes read as one edge.
    //
    // It is one absolutely positioned element on the frame now. That is a
    // stylesheet fact as much as a markup one: the day someone reaches for a
    // shadow on a cell again, this fails.
    expect(declared(".seam", "position")).toBe("absolute")
    expect(declared(".seam", "inset-block")).toBe("0")
    // The offset comes off the published width, never off a cell's own edge.
    expect(declared(".seam", "inset-inline-start")).toContain("--dt-pinned-w")
    expect(declared(".seam", "box-shadow")).toBe("var(--shadow-pinned)")
    // A rung in the ladder, not an ad-hoc number — and above the head, because
    // the seam runs the full depth of the frame, head band included.
    expect(layer(".seam")).toBeGreaterThan(layer(".head"))
    // It sits over the rows, so it must not be able to swallow a click on the
    // first unpinned column.
    expect(declared(".seam", "pointer-events")).toBe("none")
  })

  it("leaves no shadow on a cell anywhere in the sheet", () => {
    // The whole class of the defect, not just the one rule that carried it:
    // any `box-shadow` on a per-cell selector is N shadows, one per row.
    const perCell = [".cell", ".td", ".th", ".pinned", ".pinnedEdge"]
    for (const selector of perCell) {
      expect({ selector, shadow: declared(selector, "box-shadow") })
        .toEqual({ selector, shadow: undefined })
    }
    // `.pinnedEdge` is retired outright rather than left as a dead class: a
    // continuous seam already carries the hairline, and a second one a pixel
    // away is its own artefact.
    expect(SHEET).not.toContain("pinnedEdge")
  })

  it("gives the frame a containing block without opening a stacking context", () => {
    // The seam is absolute against `.root`, so `.root` has to be positioned.
    // It must stay `z-index: auto`: a stacking context here would re-rank the
    // head and the pinned cells against each other from scratch.
    expect(declared(".root", "position")).toBe("relative")
    expect(declared(".root", "z-index")).toBeUndefined()
  })

  it("leaves the port the only scrolling box in the frame", () => {
    // The whole set, checked as one: exactly one box between the frame and a
    // cell may scroll, and it is the one the pinned cells anchor to.
    const scrolls = [".root", ".scroll", ".table", ".head", ".headRow", ".body", ".row"]
      .filter((selector) =>
        ["auto", "scroll", "hidden", "overlay"].includes(
          declared(selector, "overflow") ?? ""
        )
      )
    expect(scrolls).toEqual([".scroll"])
  })
})
