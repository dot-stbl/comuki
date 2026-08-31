import { createContext, useContext, useMemo, useState } from "react"
import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import type { SourceConnection } from "@/domains/sources/model/types"
import { ConnectionsPanel } from "@/domains/sources/ui/connections-panel"
import { createSourceColumns } from "@/domains/sources/ui/sources-columns"
import { SOURCES_SEED } from "@/shared/api/mock/sources.seed"
import { SessionProvider, useSession, type Role } from "@/shared/session"

/* The seeded shift's three projects, so the panel's project column and its
   denial sentences use the words the rest of the product uses. */
const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
]

/* The source cell is a real `<Link>` to that source's own page, so the panel
   only renders inside a router. A memory router carrying the section's four
   paths gives the cells working destinations without dragging in the generated
   route tree. */
const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  [
    "/",
    "/sources",
    "/sources/new",
    "/sources/$sourceId",
    "/sources/$sourceId/ticket/new",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/sources"] }),
})

function Frame({ children }: { children: ReactNode }) {
  return (
    <SlotContext value={children}>
      <RouterProvider router={router} />
    </SlotContext>
  )
}

function Shift({
  roles,
  projectRoles = {},
  children,
}: {
  roles: Role[]
  projectRoles?: Record<string, Role[]>
  children: ReactNode
}) {
  return (
    <SessionProvider
      user={{
        id: "u_duty",
        name: "Duty Engineer",
        email: "duty@comuki.local",
        platformRoles: roles,
        projectRoles,
      }}
      projects={PROJECTS}
    >
      <Frame>{children}</Frame>
    </SessionProvider>
  )
}

/**
 * The panel as `SourcesPage` mounts it: the session is read by a component and
 * travels into the column factory as a value, because every act on a row is
 * decided against that row's project rather than against the shift.
 */
function Panel({ connections }: { connections: SourceConnection[] }) {
  const session = useSession()
  const [acted, setActed] = useState<string | null>(null)

  const columns = useMemo(
    () =>
      createSourceColumns({
        projects: session.projects,
        tickets: SOURCES_SEED.tickets,
        testingId: null,
        onOpenSource: (connection) => setActed(`open ${connection.name}`),
        onTest: (connection) => setActed(`test ${connection.name}`),
        onDisconnect: (connection) => setActed(`disconnect ${connection.name}`),
        onNewTicket: (connection) => setActed(`ticket ${connection.name}`),
        session,
      }),
    [session]
  )

  return (
    <div style={{ padding: "var(--s6)" }}>
      <ConnectionsPanel columns={columns} connections={connections} />
      {acted ? (
        <p
          style={{
            marginTop: "var(--s5)",
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            color: "var(--text-faint)",
          }}
        >
          last act: {acted}
        </p>
      ) : null}
    </div>
  )
}

const meta: Meta<typeof ConnectionsPanel> = {
  title: "Sources/Connections panel",
  component: ConnectionsPanel,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ConnectionsPanel>

/**
 * The whole seeded platform, to a role that may change all of it. Broken first,
 * then off, then working — the connection somebody came here about is the one
 * at the top.
 */
export const EveryConnection: Story = {
  render: () => (
    <Shift roles={["platform-admin"]}>
      <Panel connections={SOURCES_SEED.connections} />
    </Shift>
  ),
}

/**
 * The case the screen exists for: this person administers `atlas`, approves on
 * `comuki` and only watches `plexor`. One list, three different answers, and
 * every refused button still in place with the sentence that would open it.
 */
export const AdministersOneWatchesAnother: Story = {
  render: () => (
    <Shift
      roles={["operator"]}
      projectRoles={{
        p_comuki: ["approver"],
        p_plexor: ["viewer"],
        p_atlas: ["project-admin"],
      }}
    >
      <Panel connections={SOURCES_SEED.connections} />
    </Shift>
  ),
}

/**
 * Native intake alone — the state a project is in before anything external is
 * connected. The row is there, it cannot be disconnected, and the only act on
 * it is filing a ticket.
 */
export const NativeOnly: Story = {
  render: () => (
    <Shift roles={["platform-admin"]}>
      <Panel
        connections={SOURCES_SEED.connections.filter(
          (connection) => connection.kind === "native"
        )}
      />
    </Shift>
  ),
}

/** Nothing at all — which the product cannot actually be in, but a filter can. */
export const Empty: Story = {
  render: () => (
    <Shift roles={["platform-admin"]}>
      <Panel connections={[]} />
    </Shift>
  ),
}
