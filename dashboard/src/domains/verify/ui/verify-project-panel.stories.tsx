import { createContext, useContext } from "react"
import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { commandsFor } from "@/domains/verify/model/gate"
import type { VerifyProject } from "@/domains/verify/model/types"
import { VerifyProjectPanel } from "@/domains/verify/ui/verify-project-panel"
import { VERIFY_SEED } from "@/shared/api/mock/verify.seed"

/* Each result deep-links to the run that produced it, so the panel only renders
   inside a router. A memory router with the product's own paths gives the story
   working links without dragging in the app's route tree. */
const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/runs/$runId"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
})

function seeded(projectId: string): VerifyProject {
  return VERIFY_SEED.projects.find(
    (project) => project.projectId === projectId
  )!
}

function Panel({
  projectId,
  projectKey,
  projectName,
  denied = null,
}: {
  projectId: string
  projectKey: string
  projectName: string
  denied?: string | null
}) {
  const project = seeded(projectId)
  return (
    <SlotContext
      value={
        <div style={{ padding: "var(--s6)" }}>
          <VerifyProjectPanel
            project={project}
            projectKey={projectKey}
            projectName={projectName}
            commands={commandsFor(VERIFY_SEED.commands, projectId)}
            denied={denied}
            saving={false}
            onEnabledChange={() => {}}
          />
        </div>
      }
    >
      <RouterProvider router={router} />
    </SlotContext>
  )
}

const meta: Meta<typeof VerifyProjectPanel> = {
  title: "Verify/Project gate",
  component: VerifyProjectPanel,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof VerifyProjectPanel>

/**
 * The gate on, one check failing and one that no run has ever reached. Three
 * readings, not two — a hole in the coverage is a different fact from a broken
 * build, and the row that says so is the one nobody would otherwise notice.
 */
export const Running: Story = {
  render: () => (
    <Panel
      projectId="p_comuki"
      projectKey="comuki"
      projectName="Comuki platform"
    />
  ),
}

/**
 * The gate off. The commands are still listed, because a switch here does not
 * delete a file over there — and hiding them would suggest it had.
 */
export const GateOff: Story = {
  render: () => (
    <Panel projectId="p_plexor" projectKey="plexor" projectName="Plexor" />
  ),
}

/**
 * The gate on and the client's git declaring nothing. The empty state names the
 * file they are expected to create, because an empty box that named no file
 * would be telling them a fact they cannot act on.
 */
export const NothingDeclared: Story = {
  render: () => (
    <Panel projectId="p_atlas" projectKey="atlas" projectName="Atlas" />
  ),
}

/**
 * The switch on a project this session only watches. It stays where it was,
 * keeps its focus and its hover, and says what would open it.
 */
export const Denied: Story = {
  render: () => (
    <Panel
      projectId="p_comuki"
      projectKey="comuki"
      projectName="Comuki platform"
      denied="needs project-admin, operator or platform-admin on comuki"
    />
  ),
}
