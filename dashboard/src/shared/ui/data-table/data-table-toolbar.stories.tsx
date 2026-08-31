import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import type { DataColumn, DataTableFilterValues } from "./data-table"
import {
  DataTableToolbar,
  type DataTableToolbarProps,
} from "./data-table-toolbar"

interface Ticket {
  id: string
  app: string
  project: string
  priority: string
  profile: string
  age: number
}

/** The search column: one box that matches across everything identifying a
 *  ticket, which is what every screen in the product already declares. */
const searchColumn: DataColumn<Ticket> = {
  accessorKey: "id",
  header: "ticket",
  meta: {
    width: 144,
    filter: {
      kind: "text",
      placeholder: "search ticket, app, step…",
      match: (ticket, needle) =>
        `${ticket.id} ${ticket.app}`.toLowerCase().includes(needle.toLowerCase()),
    },
  },
}

const selectColumns: DataColumn<Ticket>[] = [
  {
    accessorKey: "app",
    header: "app",
    meta: {
      width: 128,
      filter: {
        kind: "select",
        placeholder: "all apps",
        options: [
          { value: "billing-api", label: "billing-api" },
          { value: "auth-svc", label: "auth-svc" },
          { value: "plexor", label: "plexor" },
        ],
      },
    },
  },
  {
    accessorKey: "project",
    header: "project",
    meta: {
      width: 128,
      filter: {
        kind: "select",
        placeholder: "all projects",
        options: [
          { value: "atlas", label: "atlas" },
          { value: "orbit", label: "orbit" },
        ],
      },
    },
  },
  {
    accessorKey: "priority",
    header: "priority",
    meta: {
      width: 96,
      filter: {
        kind: "select",
        placeholder: "all priority",
        options: [
          { value: "waiting", label: "waiting" },
          { value: "running", label: "running" },
          { value: "queued", label: "queued" },
        ],
      },
    },
  },
  {
    accessorKey: "profile",
    header: "profile",
    meta: {
      width: 120,
      filter: {
        kind: "select",
        placeholder: "all profiles",
        options: [
          { value: "planner", label: "planner" },
          { value: "builder", label: "builder" },
        ],
      },
    },
  },
]

const ageColumn: DataColumn<Ticket> = {
  accessorKey: "age",
  header: "age min",
  meta: { width: 96, numeric: true },
}

/** The ordinary shape: one text filter and a handful of selects. */
const columns: DataColumn<Ticket>[] = [
  ...selectColumns.slice(0, 2),
  searchColumn,
  ...selectColumns.slice(2),
  ageColumn,
]

/** The toolbar is controlled; the story holds the state a screen would hold. */
function ToolbarDemo({
  initialFilters = {},
  ...props
}: Omit<DataTableToolbarProps<Ticket>, "filters" | "onFiltersChange"> & {
  initialFilters?: DataTableFilterValues
}) {
  const [filters, setFilters] = useState<DataTableFilterValues>(initialFilters)
  const [visibility, setVisibility] = useState(props.columnVisibility ?? {})

  return (
    <DataTableToolbar
      {...props}
      filters={filters}
      onFiltersChange={setFilters}
      columnVisibility={props.columnVisibility ? visibility : undefined}
      onColumnVisibilityChange={
        props.columnVisibility ? setVisibility : undefined
      }
    />
  )
}

const meta = {
  title: "UI Kit/Data/DataTableToolbar",
  component: ToolbarDemo,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  args: { columns },
} satisfies Meta<typeof ToolbarDemo>

export default meta
type Story = StoryObj<typeof meta>

/**
 * Nothing filtered: the search field and the button, and no chip strip at all.
 * The row reserves no height for a reading it does not have.
 */
export const Empty: Story = {}

/** One filter on: one chip, and the button counts it. */
export const OneFilter: Story = {
  args: { initialFilters: { priority: "waiting" } },
}

/**
 * Four on. Chips grow to the right and wrap; the search field and the button
 * are exactly where they were when the row was empty.
 */
export const ManyFilters: Story = {
  args: {
    initialFilters: {
      priority: "waiting",
      app: "plexor",
      profile: "planner",
      project: "atlas",
    },
  },
}

/** Typing in the row's search field drives the promoted text filter — which
 *  therefore never appears in the popover and never earns a chip. */
export const Searching: Story = {
  args: { initialFilters: { id: "billing" } },
}

/**
 * A screen whose only filter is that one text filter — projects, today. There
 * is nothing to put in a popover, so there is no button.
 */
export const SearchOnly: Story = {
  args: { columns: [searchColumn, ageColumn] },
}

/**
 * A screen that declares no text filter — role routing, today. No search
 * field; the button is the row's left edge.
 */
export const NoSearch: Story = {
  args: {
    columns: [...selectColumns.slice(0, 2), ageColumn],
    initialFilters: { app: "auth-svc" },
  },
}

/** With the column manager folded into the tail. Which columns exist is not
 *  which rows show, so it sits apart from the filters and out of the count. */
export const WithColumnManager: Story = {
  args: { columnVisibility: {}, initialFilters: { priority: "waiting" } },
}

export const WithSlots: Story = {
  args: {
    columnVisibility: {},
    initialFilters: { app: "plexor" },
    leading: <span>3 selected</span>,
    trailing: <span>128 shown</span>,
  },
}
