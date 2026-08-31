import { useMemo, useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { StatusBadge, type Status } from "../status-badge"
import {
  DataTable,
  applyDataFilters,
  emptyFilterValues,
  rankSort,
  type DataColumn,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableProps,
  type DataTableRowSelection,
  type DataTableSorting,
} from "./data-table"
import { DataTableToolbar } from "./data-table-toolbar"

interface Shard {
  id: string
  worker: string
  profile: string
  status: Status
  tokens: number
  latency: number
  cost: number
}

const PROFILES = ["plan", "edit", "verify", "review"]
const STATUSES: Status[] = ["running", "success", "failed", "waiting", "queued"]

/** Deterministic fixtures — a story that reshuffles on reload cannot be read. */
function makeShards(count: number): Shard[] {
  return Array.from({ length: count }, (_, index) => ({
    id: `shard-${String(index + 1).padStart(4, "0")}`,
    worker: `pi-${String((index % 12) + 1).padStart(2, "0")}`,
    profile: PROFILES[index % PROFILES.length] ?? "plan",
    status: STATUSES[index % STATUSES.length] ?? "queued",
    tokens: 1200 + ((index * 977) % 88000),
    latency: 40 + ((index * 613) % 9600),
    cost: Number((0.02 + ((index * 37) % 480) / 1000).toFixed(3)),
  }))
}

const SHARDS = makeShards(24)
const MANY_SHARDS = makeShards(240)

const columns: DataColumn<Shard>[] = [
  {
    accessorKey: "id",
    header: "shard",
    meta: { width: 144 },
  },
  {
    accessorKey: "worker",
    header: "worker",
    meta: {
      width: 112,
      filter: { kind: "text", placeholder: "filter worker…" },
    },
  },
  {
    accessorKey: "profile",
    header: "profile",
    meta: {
      width: 112,
      filter: {
        kind: "select",
        placeholder: "all profiles",
        options: PROFILES.map((profile) => ({ value: profile, label: profile })),
      },
    },
  },
  {
    accessorKey: "status",
    header: "status",
    cell: ({ row }) => (
      <StatusBadge status={row.original.status} size="sm">
        {row.original.status}
      </StatusBadge>
    ),
    meta: {
      width: 136,
      filter: {
        kind: "select",
        placeholder: "all statuses",
        options: STATUSES.map((status) => ({ value: status, label: status })),
      },
    },
  },
  {
    accessorKey: "tokens",
    header: "tokens",
    cell: ({ row }) => row.original.tokens.toLocaleString("en-US"),
    meta: { width: 112, numeric: true },
  },
  {
    accessorKey: "latency",
    header: "latency ms",
    cell: ({ row }) => row.original.latency.toLocaleString("en-US"),
    meta: { width: 112, numeric: true },
  },
  {
    accessorKey: "cost",
    header: "cost usd",
    cell: ({ row }) => row.original.cost.toFixed(3),
    meta: { width: 96, numeric: true },
  },
]

const getRowId = (row: Shard) => row.id

const meta = {
  title: "UI Kit/Data/DataTable",
  component: DataTable<Shard>,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  args: {
    columns,
    data: SHARDS,
    getRowId,
  },
  argTypes: {
    density: { control: "radio", options: ["compact", "comfortable"] },
  },
} satisfies Meta<typeof DataTable<Shard>>

export default meta
type Story = StoryObj<typeof meta>

/**
 * The default. A row is `--h-row-head` deep — the same rhythm as the head band
 * above it — so the frame reads as one instrument rather than as a header
 * standing over a tighter list. Density varies the cell gutter and the type
 * step; the row's depth is published as `--dt-row-h` from the same number the
 * virtualizer offsets by, so the two can never drift.
 */
export const Compact: Story = {
  args: { density: "compact" },
}

/** One step of the spacing scale roomier, for a table that is the whole screen. */
export const Comfortable: Story = {
  args: { density: "comfortable" },
}

/** The duty target: 50–200 concurrent runs, scrolled rather than paged. */
export const ManyRows: Story = {
  name: "240 rows (virtualized)",
  args: { data: MANY_SHARDS, bodyHeight: "32rem" },
}

export const Empty: Story = {
  args: { data: [], emptyLabel: "No shards claimed" },
}

function SelectableTable(props: DataTableProps<Shard>) {
  const [selected, setSelected] = useState<DataTableRowSelection>({
    "shard-0002": true,
    "shard-0005": true,
  })

  return (
    <DataTable
      {...props}
      selection={{ value: selected, onChange: setSelected, noun: "shard" }}
    />
  )
}

export const WithSelection: Story = {
  render: (args) => <SelectableTable {...args} />,
}

function FilteredTable(props: DataTableProps<Shard>) {
  const [filters, setFilters] = useState<DataTableFilterValues>(() =>
    emptyFilterValues(columns)
  )
  const [visibility, setVisibility] = useState<DataTableColumnVisibility>({})

  const rows = useMemo(
    () => applyDataFilters(props.data, filters, columns),
    [props.data, filters]
  )

  return (
    <div>
      <DataTableToolbar
        columns={columns}
        filters={filters}
        onFiltersChange={setFilters}
        columnVisibility={visibility}
        onColumnVisibilityChange={setVisibility}
        trailing={<span>{rows.length} shown</span>}
      />
      <DataTable
        {...props}
        data={rows}
        columnVisibility={visibility}
        onColumnVisibilityChange={setVisibility}
      />
    </div>
  )
}

/** Toolbar + table: one column declaration drives both. */
export const WithToolbar: Story = {
  args: { data: MANY_SHARDS, bodyHeight: "28rem" },
  render: (args) => <FilteredTable {...args} />,
}

/** Worst first. Alphabetically `failed` would land between `escalated` and
 *  `queued`, which is not what a shard board means by order. */
const STATUS_RANK: Record<string, number> = {
  failed: 0,
  waiting: 1,
  running: 2,
  queued: 3,
  success: 4,
}

const sortableColumns: DataColumn<Shard>[] = [
  ...columns.map((column) =>
    (column as { accessorKey?: string }).accessorKey === "status"
      ? { ...column, sortFn: rankSort(STATUS_RANK) }
      : column
  ),
  {
    id: "actions",
    header: "actions",
    enableSorting: false,
    cell: () => <span>retry</span>,
    meta: { width: 96, align: "end", label: "actions" },
  },
]

function SortedTable(props: DataTableProps<Shard>) {
  // Screen-owned, exactly as a page holds it — the table never keeps a copy.
  const [sorting, setSorting] = useState<DataTableSorting>([
    { id: "latency", desc: true },
  ])

  return (
    <DataTable
      {...props}
      columns={sortableColumns}
      sorting={sorting}
      onSortingChange={setSorting}
    />
  )
}

/**
 * Sortable head. Clicking a column cycles ascending → descending → none, and
 * the chevron only inks in on the column doing the sorting or the one under
 * the pointer. `status` sorts by rank rather than by spelling, the numeric
 * columns compare as numbers because their `meta` says they are numbers, and
 * `actions` — a column with nothing to compare — has no button at all.
 */
export const WithSorting: Story = {
  name: "Sortable head",
  args: { data: MANY_SHARDS, bodyHeight: "28rem" },
  render: (args) => <SortedTable {...args} />,
}

/* Resizable and pinned tracks ------------------------------------------- */

/**
 * Extra tracks, so the port actually runs out of room on any monitor. Neither
 * feature says anything until the sum of the columns is wider than the box
 * holding them: pinning is invisible in a table that fits, and a resize that
 * cannot push the table past its port only moves slack around.
 */
const extraColumns: DataColumn<Shard>[] = [
  {
    id: "host",
    header: "host",
    accessorFn: (row) => `${row.worker}.dc1`,
    meta: { width: 160, label: "host" },
  },
  {
    id: "branch",
    header: "branch",
    accessorFn: (row) => `feat/${row.id}`,
    meta: { width: 240, label: "branch" },
  },
  {
    id: "queue",
    header: "queue",
    accessorFn: (row) => `${row.profile}-lane`,
    meta: { width: 160, label: "queue" },
  },
  {
    id: "image",
    header: "image",
    accessorFn: (row) => `ghcr.io/comuki/pi:${row.profile}`,
    meta: { width: 288, label: "image" },
  },
  {
    id: "lease",
    header: "lease",
    accessorFn: (row) => `${row.worker}/${row.id}`,
    meta: { width: 240, label: "lease" },
  },
]

/** `actions` stays the end column: the extras go in front of it, not after. */
const ACTIONS_INDEX = sortableColumns.length - 1
const wideColumns: DataColumn<Shard>[] = [
  ...sortableColumns.slice(0, ACTIONS_INDEX),
  ...extraColumns,
  ...sortableColumns.slice(ACTIONS_INDEX),
]

/** The same set with the first column pinned — the flag, not a position. */
const pinnedColumns: DataColumn<Shard>[] = wideColumns.map((column, index) =>
  index === 0 ? { ...column, meta: { ...column.meta, pinned: true } } : column
)

function ResizableTable(props: DataTableProps<Shard>) {
  // Screen-owned, exactly like sorting and visibility: the table never keeps a
  // copy, and this is the object a screen would persist.
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({
    worker: 160,
  })

  return (
    <div>
      <p>
        {Object.keys(columnSizing).length === 0
          ? "every column at its declared width"
          : Object.entries(columnSizing)
              .map(([id, width]) => `${id} ${Math.round(width)}px`)
              .join(" · ")}
      </p>
      <DataTable
        {...props}
        columns={wideColumns}
        columnSizing={columnSizing}
        onColumnSizingChange={setColumnSizing}
      />
    </div>
  )
}

/**
 * Drag a head's end edge, or focus the grip and use the arrow keys — Shift for
 * a coarse step. Double-click, Enter or Space put a column back to the width it
 * declared, which is what makes `meta.width` an opening position rather than a
 * permanent one. `worker` starts pre-sized to show that the slice is the
 * screen's: it arrives from state, not from the column list, and the line above
 * the table is that state read back.
 *
 * `task` and `step` declare no width. They share the port's slack the way they
 * always have, and stop growing the moment they are dragged — after that they
 * are a width like every other column.
 */
export const WithResizing: Story = {
  name: "Resizable columns",
  args: { data: MANY_SHARDS, bodyHeight: "24rem" },
  render: (args) => <ResizableTable {...args} />,
}

/**
 * `shard` carries `meta.pinned`, so it stays against the start edge while the
 * rest of the table scrolls under it. The seam — a hairline and a short shadow
 * — appears only once the port has actually moved, so a table sitting at its
 * start edge reads as columns side by side rather than as two planes.
 */
export const WithPinnedColumn: Story = {
  name: "Pinned column",
  args: {
    data: MANY_SHARDS,
    columns: pinnedColumns,
    bodyHeight: "24rem",
  },
}

function PinnedResizableTable(props: DataTableProps<Shard>) {
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  return (
    <DataTable
      {...props}
      columns={pinnedColumns}
      columnSizing={columnSizing}
      onColumnSizingChange={setColumnSizing}
    />
  )
}

/**
 * Both at once, which is where they have to agree: widening the pinned column
 * moves the seam and every unpinned track with it, because the head, the body
 * and the table's own inline size all read the one width source.
 */
export const WithPinnedAndResizing: Story = {
  name: "Pinned + resizable",
  args: { data: MANY_SHARDS, bodyHeight: "24rem" },
  render: (args) => <PinnedResizableTable {...args} />,
}
