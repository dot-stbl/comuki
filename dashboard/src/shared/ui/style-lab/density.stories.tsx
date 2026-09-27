import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import {
  DataTable,
  Section,
  StatusBadge,
  type DataColumn,
  type DataTableDensity,
} from "@/shared/ui"

/* Density is the lab's subject: the same rows, read twice. The kit's table
   carries a `density` prop — `compact` and `comfortable` are its two stops,
   the row height moving from the working step to the breathing one — and
   the kit's lists carry the same decision as spacing between rows. A
   specimen that invented a third stop would be a knob nobody can set; this
   page shows exactly the two the product ships. */

interface Shard {
  readonly run: string
  readonly profile: string
  readonly status: Parameters<typeof StatusBadge>[0]["status"]
  readonly worker: string
  readonly cost: string
}

/** Deterministic rows — a density specimen that reshuffled on reload could
    not be compared against its own other half. */
const SHARDS: readonly Shard[] = [
  { run: "run-0af1", profile: "plan", status: "running", worker: "pi-04", cost: "0.412" },
  { run: "run-0b52", profile: "edit", status: "waiting", worker: "pi-07", cost: "1.208" },
  { run: "run-0c13", profile: "verify", status: "failed", worker: "pi-02", cost: "0.097" },
  { run: "run-0d94", profile: "review", status: "success", worker: "pi-11", cost: "2.651" },
  { run: "run-0e35", profile: "plan", status: "queued", worker: "pi-04", cost: "0.000" },
  { run: "run-0f76", profile: "edit", status: "escalated", worker: "pi-09", cost: "0.883" },
]

const COLUMNS: DataColumn<Shard>[] = [
  { accessorKey: "run", header: "run", meta: { width: 128 } },
  { accessorKey: "profile", header: "profile", meta: { width: 112 } },
  {
    accessorKey: "status",
    header: "status",
    cell: ({ row }) => (
      <StatusBadge status={row.original.status} size="sm">
        {row.original.status}
      </StatusBadge>
    ),
    meta: { width: 136 },
  },
  { accessorKey: "worker", header: "worker", meta: { width: 104 } },
  {
    accessorKey: "cost",
    header: "cost usd",
    cell: ({ row }) => row.original.cost,
    meta: { width: 104, numeric: true },
  },
]

const getRowId = (row: Shard) => row.run

/** One half of the comparison: the table at one density, under a heading
    that says which stop it is. */
function TableHalf({ density }: { readonly density: DataTableDensity }): ReactNode {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "var(--s4)", flex: 1, minWidth: 0 }}>
      <span
        style={{
          fontFamily: "var(--font-data)",
          fontSize: "var(--t-micro)",
          letterSpacing: "var(--tracking-label)",
          color: "var(--text-faint)",
        }}
      >
        {`density="${density}"`}
      </span>
      <DataTable
        columns={COLUMNS}
        data={[...SHARDS]}
        getRowId={getRowId}
        density={density}
      />
    </div>
  )
}

/** A row of the list comparison — the same facts the table carries, as the
    list reading the rail and the panels use. */
function ListRow({ shard }: { readonly shard: Shard }): ReactNode {
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: "var(--s4)",
        padding: "var(--s3) var(--s4)",
        borderBottom: "var(--hairline) solid var(--rule)",
        fontFamily: "var(--font-data)",
        fontSize: "var(--t-xs)",
        color: "var(--text)",
      }}
    >
      <StatusBadge status={shard.status} size="sm">
        {shard.status}
      </StatusBadge>
      <span>{shard.run}</span>
      <span style={{ color: "var(--text-muted)" }}>{shard.profile}</span>
      <span style={{ marginLeft: "auto", color: "var(--text-muted)" }}>
        {shard.worker}
      </span>
    </div>
  )
}

/** The list half: the same six rows at the working step and at the
    breathing step. The list has no `density` prop to point at — its density
    IS the gap and the row padding, so the specimen says which spacing
    tokens each half uses and nothing else differs. */
function ListHalf({
  gap,
  label,
  rows,
}: {
  readonly gap: string
  readonly label: string
  readonly rows: readonly Shard[]
}): ReactNode {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "var(--s4)", flex: 1, minWidth: 0 }}>
      <span
        style={{
          fontFamily: "var(--font-data)",
          fontSize: "var(--t-micro)",
          letterSpacing: "var(--tracking-label)",
          color: "var(--text-faint)",
        }}
      >
        {label}
      </span>
      <div style={{ display: "flex", flexDirection: "column", gap }}>
        {rows.map((shard) => (
          <ListRow key={shard.run} shard={shard} />
        ))}
      </div>
    </div>
  )
}

function Page({ children }: { readonly children: ReactNode }): ReactNode {
  return (
    <div
      style={{
        padding: "var(--page-y) var(--page-x)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--s8)",
        maxWidth: "var(--measure-record)",
        margin: "0 auto",
      }}
    >
      {children}
    </div>
  )
}

const meta: Meta = {
  title: "UI Kit/Style Lab/Density",
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj

/** The table's two stops, side by side on the same rows. `compact` is the
    duty screen's reading — the row is a unit of scan; `comfortable` is the
    report reading — the row is a unit of read. Nothing between them exists,
    and the specimen is the argument for why not: a middle stop would be a
    knob with no sentence behind it. */
export const Table: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Table density"
        note="The kit's DataTable carries exactly two densities. Compact rows pack the shift's whole swarm on one screen; comfortable rows give the same rows room to be read one at a time. The prop is the whole decision — no per-screen row-height tuning exists, and a table that needed one would be a table asking for the wrong density."
      >
        <div style={{ display: "flex", gap: "var(--s8)", alignItems: "flex-start" }}>
          <TableHalf density="compact" />
          <TableHalf density="comfortable" />
        </div>
      </Section>
    </Page>
  ),
}

/** The list's two readings on the same rows. A list's density is not a prop
    — it is the spacing step between rows, so the specimen pins the two
    stops the product uses: the working step (`--s2`, the rows reading as
    one list) and the breathing step (`--s5`, the rows reading as separate
    items). The gap is labelled because it is the knob. */
export const List: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="List density"
        note="Where the table states its density, the list wears it as spacing. The tight reading uses the working step between rows, so the eye takes the list as one instrument; the relaxed reading uses the breathing step, so each row stands as its own item. Everything else on the row is identical — the specimen changes one token and nothing else."
      >
        <div style={{ display: "flex", gap: "var(--s8)", alignItems: "flex-start" }}>
          <ListHalf
            gap="var(--s2)"
            label="gap: --s2 · the working step"
            rows={SHARDS}
          />
          <ListHalf
            gap="var(--s5)"
            label="gap: --s5 · the breathing step"
            rows={SHARDS}
          />
        </div>
      </Section>
    </Page>
  ),
}