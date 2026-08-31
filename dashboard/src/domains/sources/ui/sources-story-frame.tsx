import { createContext, useContext, useEffect, type ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { ThemeProvider } from "@/app/theme-provider"
import { PROJECTS_SEED } from "@/shared/api/mock/session.seed"
import { resetSeedSources } from "@/shared/api/mock/sources.store"
import { SessionProvider, type Role } from "@/shared/session"

/**
 * The three things a Sources screen cannot render without, in one place.
 *
 * Every page in this section draws the whole shell, links to the product's own
 * paths and reads the mutable store, so a story of one needs a router, a
 * session and a query client — the same three the app hands it. Three story
 * files were about to carry a byte-identical copy of this, and the copies would
 * have drifted on the one thing that is not a detail: which projects the
 * session can see. The denial sentences on these screens name a project by the
 * key the operator calls it, so the frame hands over the *seeded* three rather
 * than a pair invented for a story.
 *
 * A memory router carrying the product's own path list gives working crumbs and
 * working hand-offs without dragging in the generated route tree — which a
 * story must not import, because it is a build artefact and a story is a
 * document.
 *
 * It lives beside the components rather than in a test folder because it is
 * exactly what `connections-panel.stories.tsx` already spelled privately, and
 * it exports nothing but components.
 */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null

const routeTree = rootRoute.addChildren(
  [
    "/",
    "/chat",
    "/tasks",
    "/runs",
    "/queue",
    "/approvals",
    "/cost",
    "/sources",
    "/sources/new",
    "/sources/$sourceId",
    "/sources/$sourceId/ticket/new",
    "/knowledge",
    "/verify",
    "/settings",
    "/projects",
    "/projects/$projectId",
    "/identity",
    "/compute",
    "/models",
    "/observability",
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

export interface SourcesStoryFrameProps {
  children: ReactNode
  /** Platform roles for the shift. Defaults to the role that may do it all. */
  roles?: Role[]
  /** Roles held on one project — where the interesting refusals live. */
  projectRoles?: Record<string, Role[]>
  /** The address the story is standing at, so crumbs point somewhere real. */
  at?: string
}

export function SourcesStoryFrame({
  children,
  roles = ["platform-admin"],
  projectRoles = {},
  at = "/sources",
}: SourcesStoryFrameProps) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [at] }),
  })

  return (
    <ThemeProvider defaultTheme="dark" storageKey="comuki-story-theme">
      <SessionProvider
        user={{
          id: "u_duty",
          name: "Duty Engineer",
          email: "duty@comuki.local",
          platformRoles: roles,
          projectRoles,
        }}
        projects={PROJECTS_SEED}
      >
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          <SlotContext value={children}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </SessionProvider>
    </ThemeProvider>
  )
}

/**
 * The seeded store, restored on the way in and on the way out.
 *
 * These screens write: connecting, disconnecting, saving a watch. Without this
 * a story is whatever the story before it left behind, which is the one kind of
 * document that is worse than none.
 */
export function SeededSources({ children }: { children: ReactNode }) {
  useEffect(() => {
    resetSeedSources()
    return () => {
      resetSeedSources()
    }
  }, [])
  return <>{children}</>
}
