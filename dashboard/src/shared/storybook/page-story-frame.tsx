import {
  createContext,
  useContext,
  useState,
  type ReactNode,
} from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { SessionProvider, type ProjectRef } from "@/shared/session"

export interface PageStoryFrameProps {
  /** Paths the memory router must declare so `<Link>`s in crumbs resolve. */
  readonly routes: readonly string[]
  /** The entry the history is opened on — the screen's own path. */
  readonly initialPath: string
  /** Projects the `SessionProvider` exposes — Comuki + 0..N more. */
  readonly projects: readonly ProjectRef[]
  /** Pin the React Query cache before the page mounts (empty states, etc). */
  readonly seed?: (client: QueryClient) => void
  readonly children: ReactNode
}

const SlotContext = createContext<ReactNode>(null)

function Slot(): ReactNode {
  return <>{useContext(SlotContext)}</>
}

/**
 * The harness every page story sits inside.
 *
 * The shell draws the whole rail and answers its own queries, so the story
 * needs the same three the app hands every screen: a router (the rail's
 * `<Link>`s), a session (the rail's visibility) and a query client. The
 * memory router is the cheapest way to give the rail real paths without
 * dragging in the generated route tree; `form-page.stories.tsx` is the
 * reference this follows.
 *
 * `seed` runs once on the `QueryClient` before the `QueryClientProvider`
 * mounts, so a story that wants an empty cache pins it before any reader
 * sees it — no refetch, no flicker, no second-render correction.
 */
export function PageStoryFrame({
  routes,
  initialPath,
  projects,
  seed,
  children,
}: PageStoryFrameProps): ReactNode {
  const rootRoute = createRootRoute({ component: Slot })
  const blank = (): null => null

  const routeTree = rootRoute.addChildren(
    routes.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    )
  )

  // boundary: non-generic createRouter returns a router without a typed route
  // map; RouterProvider accepts the AnyRouter shape but TS narrows it to the
  // generic, so the cast sits at the one place it can sit.
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [initialPath] }),
  }) as unknown as Parameters<typeof RouterProvider>[0]["router"]

  const [client] = useState(() => {
    const built = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: Infinity } },
    })
    seed?.(built)
    return built
  })

  return (
    <SessionProvider
      user={{
        id: "u_story",
        name: "Rhea Okafor",
        email: "rhea@comuki.local",
        platformRoles: ["platform-admin"],
        projectRoles: {},
      }}
      projects={[...projects]}
    >
      <QueryClientProvider client={client}>
        <SlotContext value={children}>
          <RouterProvider router={router} />
        </SlotContext>
      </QueryClientProvider>
    </SessionProvider>
  )
}