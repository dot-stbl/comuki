import { createContext, useContext, useState, type ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { SessionProvider } from "@/shared/session"
import {
  DataTable,
  Section,
  StatusBadge,
  type DataColumn,
} from "@/shared/ui"

/* The layout lab: the shell's own frame — rail, topbar, pinned header —
   around the three width readings a screen's content can take. The product
   has no PageTemplate with a width prop; it has measure tokens, and every
   screen picks one deliberately. This specimen puts the three picks side by
   side on real blocks, so a new screen's choice is a comparison rather than
   a guess.

   The shell draws the whole rail and answers its own queries, so the story
   needs the same three the app hands every screen: a router (the rail's
   links), a session (the rail's visibility) and a query client. The memory
   router is the pattern the page stories use. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null

const routeTree = rootRoute.addChildren(
  [
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
    "/projects",
    "/compute",
    "/models",
    "/observability",
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/queue"] }),
})

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
]

function Frame({ children }: { children: ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: { queries: { retry: false, staleTime: Infinity } },
      })
  )

  return (
    <SessionProvider
      user={{
        id: "u_story",
        name: "Rhea Okafor",
        email: "rhea@comuki.local",
        platformRoles: ["platform-admin"],
        projectRoles: {},
      }}
      projects={PROJECTS}
    >
      <QueryClientProvider client={client}>
        <SlotContext value={children}>
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </SlotContext>
      </QueryClientProvider>
    </SessionProvider>
  )
}

interface PoolRow {
  readonly pool: string
  readonly region: string
  readonly status: Parameters<typeof StatusBadge>[0]["status"]
  readonly busy: string
}

const POOL_ROWS: readonly PoolRow[] = [
  { pool: "pi-eu-west", region: "eu-west-1", status: "running", busy: "7 / 8" },
  { pool: "pi-eu-north", region: "eu-north-1", status: "waiting", busy: "2 / 4" },
  { pool: "pi-us-east", region: "us-east-1", status: "queued", busy: "0 / 8" },
  { pool: "pi-lab", region: "on-prem", status: "failed", busy: "0 / 2" },
]

const POOL_COLUMNS: DataColumn<PoolRow>[] = [
  { accessorKey: "pool", header: "pool", meta: { width: 160 } },
  { accessorKey: "region", header: "region", meta: { width: 128 } },
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
  {
    accessorKey: "busy",
    header: "busy",
    cell: ({ row }) => row.original.busy,
    meta: { width: 96, numeric: true },
  },
]

/** The measure label every block carries — the token IS the decision, so
    the specimen says it out loud beside the block it governs. */
function MeasureTag({ token }: { readonly token: string }): ReactNode {
  return (
    <span
      style={{
        fontFamily: "var(--font-data)",
        fontSize: "var(--t-micro)",
        letterSpacing: "var(--tracking-data)",
        color: "var(--text-faint)",
      }}
    >
      {token}
    </span>
  )
}

/** One width reading: the same three blocks every reading carries — a
    prose section, a fact strip, a table — held to one measure. Only the
    measure differs between readings; the blocks are identical so the eye
    compares widths and nothing else. */
function Reading({
  token,
  measure,
  title,
  note,
}: {
  readonly token: string
  readonly measure: string
  readonly title: string
  readonly note: string
}): ReactNode {
  return (
    <div style={{ maxWidth: measure, display: "flex", flexDirection: "column", gap: "var(--s5)" }}>
      <MeasureTag token={token} />
      <Section variant="screen" title={title} note={note}>
        <div
          style={{
            display: "flex",
            gap: "var(--s5)",
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-xs)",
            color: "var(--text-muted)",
          }}
        >
          <span>4 pools</span>
          <span>17 workers</span>
          <span>2 regions</span>
          <span>depth 3</span>
        </div>
      </Section>
      <DataTable
        columns={POOL_COLUMNS}
        data={[...POOL_ROWS]}
        getRowId={(row) => row.pool}
      />
    </div>
  )
}

export default {
  title: "Compositions/Style Lab/Layout",
  parameters: { layout: "fullscreen" },
}

/** The shell in full — rail, topbar, pinned header — with the three width
    readings stacked in the scroll port. The form measure is a controlled
    block of inputs; the record measure is the one width every detail page
    uses; the wide reading is the full port at the page gutter, which is
    where the duty tables live. A screen that could not say which of the
    three it is has not chosen yet. */
export function WidthReadings(): ReactNode {
  return (
    <Frame>
      <AppShell
        header={
          <PageHeader
            breadcrumbs={[{ label: "Observe", to: "/runs" }, { label: "Queue" }]}
            title="Layout lab"
            summary="Three measures, one scroll port, the same blocks in each"
          />
        }
      >
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "var(--s8)",
            paddingTop: "var(--s6)",
          }}
        >
          <Reading
            token="--measure-form · 44rem"
            measure="var(--measure-form)"
            title="The form measure"
            note="A controlled block of inputs — the width a form page holds its fields to, so a label never drifts far from the field it names."
          />
          <Reading
            token="--measure-record · 60rem"
            measure="var(--measure-record)"
            title="The record measure"
            note="One record's page — the single width every detail screen uses, so two detail screens never disagree about where a fact sits."
          />
          <Reading
            token="the scroll port, at the page gutter"
            measure="none"
            title="The wide reading"
            note="The full port at --page-x — the duty tables' reading, where a row is a unit of scan and the table earns the whole width the rail leaves."
          />
        </div>
      </AppShell>
    </Frame>
  )
}
