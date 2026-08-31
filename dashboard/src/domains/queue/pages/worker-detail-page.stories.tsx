import { createContext, useContext, useEffect, useState } from "react"
import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { SessionProvider } from "@/shared/session"

import { queueQueryKey, type QueueBoard } from "@/domains/queue/api/queries"
import type { QueueItem, Worker } from "@/domains/queue/model/types"

import { WorkerDetailPage } from "./worker-detail-page"

/* The page is a screen: it renders the whole shell, its crumbs are real
   `<Link>`s and its hand-offs are real addresses. So it only exists inside a
   router, a session and a query client — the same three the app hands it. A
   memory router carrying the product's own paths gives the story working
   crumbs without dragging in the route tree, exactly as `form-page.stories`
   does.

   The board is *written into the cache* rather than fetched. Each story is
   then a stated shift — a container with six seconds of lease left, a pool
   that has just torn one down — instead of whatever the seed happens to hold
   today, and the two readings that only exist as a *transition* become
   reachable at all. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null

const routeTree = rootRoute.addChildren([
  ...[
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
  ),
  createRoute({
    getParentRoute: () => rootRoute,
    path: "/runs/$runId",
    component: blank,
  }),
])

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
})

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
]

const POOLS = [{ projectId: "p_comuki", minIdle: 2, maxIdle: 6 }]

const IMAGE = "sha256:9c41ab"

function worker(over: Partial<Worker> = {}): Worker {
  return {
    id: "wk_2f8a",
    projectId: "p_comuki",
    profile: "implementer",
    state: "busy",
    itemId: "wi_0101",
    provider: "docker",
    handle: "docker/comuki-dev/2f8a91c4",
    heartbeatAgeSec: 3,
    leaseSec: 214,
    upSec: 1840,
    digest: IMAGE,
    ...over,
  }
}

function item(over: Partial<QueueItem> = {}): QueueItem {
  return {
    id: "wi_0101",
    runId: "8f3c2a91",
    projectId: "p_comuki",
    profile: "implementer",
    label: "перенести расчёт выплат на новый прайс",
    status: "running",
    ageSec: 412,
    claimedBy: "wk_2f8a",
    blockedOn: [],
    ...over,
  }
}

function board(over: Partial<QueueBoard> = {}): QueueBoard {
  return { items: [item()], workers: [worker()], pools: POOLS, ...over }
}

interface ScreenProps {
  workerId: string
  /** The shift the page opens on. */
  data: QueueBoard
  /**
   * The refetch that no longer carries the container.
   *
   * Applied once after mount, which is exactly the sequence the torn-down
   * state exists for: the page saw the worker, held it in a ref, and then the
   * payload stopped carrying it. Nothing else produces that reading — a story
   * that simply opened on a board without the worker is the *not-found* one,
   * which is the story below it.
   */
  after?: QueueBoard
}

function Screen({ workerId, data, after }: ScreenProps) {
  const [client] = useState(() => {
    const created = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
          // No refetch behind the story's back: the cache *is* the shift.
          staleTime: Infinity,
          refetchOnMount: false,
          refetchOnWindowFocus: false,
        },
      },
    })
    created.setQueryData(queueQueryKey, data)
    return created
  })

  useEffect(() => {
    if (after) {
      client.setQueryData(queueQueryKey, after)
    }
  }, [client, after])

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
        <SlotContext value={<WorkerDetailPage workerId={workerId} />}>
          <RouterProvider router={router} />
        </SlotContext>
      </QueryClientProvider>
    </SessionProvider>
  )
}

/**
 * One worker, at `/queue/workers/<id>`.
 *
 * The one detail page in this product with *time* in it. A lease expires, a
 * heartbeat ages, and a container is ephemeral by design — so the screen is
 * three clocks, the image the container came up on, and the single item it
 * holds a lease on. Everything else is handed off with an address the product
 * already mints: `/queue?w=<digest>`, `/queue?q=<itemId>`, `/runs/<id>`. There
 * is no queue table here and there is not going to be one — a detail page
 * links to the real screens with a filter applied rather than redrawing them.
 */
const meta: Meta<typeof WorkerDetailPage> = {
  title: "Queue/WorkerDetailPage",
  component: WorkerDetailPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof WorkerDetailPage>

/** A container holding an item, heartbeating, most of its lease still ahead. */
export const Live: Story = {
  render: () => <Screen workerId="wk_2f8a" data={board()} />,
}

/**
 * The failure the pool exists to catch, seen from the container's own page:
 * six seconds of lease left and no heartbeat for over a minute. Nobody may
 * claim the item until the lease lapses, and the sentence under the readings
 * says out loud what happens when it does — the same wording the pool's lease
 * column carries in a tooltip, because there is one of it.
 */
export const LostHeartbeat: Story = {
  render: () => (
    <Screen
      workerId="wk_e34d"
      data={board({
        workers: [
          worker({
            id: "wk_e34d",
            projectId: "p_plexor",
            provider: "kubernetes",
            handle: "k8s/plexor-prod/worker-implementer-e34d",
            heartbeatAgeSec: 74,
            leaseSec: 6,
            upSec: 1512,
            itemId: "wi_0104",
          }),
        ],
        items: [
          item({
            id: "wi_0104",
            projectId: "p_plexor",
            runId: "b3d8a402",
            label: "починить ретраи вебхука на 5xx",
            claimedBy: "wk_e34d",
            ageSec: 1186,
          }),
        ],
      })}
    />
  ),
}

/**
 * Idle is the pool doing its job, not a gap — so it is said in the same word
 * the pool's own column says it in, and nothing here is dressed as a problem.
 * There is no lease to draw and the meter says so rather than drawing a zero.
 */
export const Idle: Story = {
  render: () => (
    <Screen
      workerId="wk_a07e"
      data={board({
        workers: [
          worker({
            id: "wk_a07e",
            profile: "explorer",
            state: "idle",
            itemId: null,
            handle: "docker/comuki-dev/a07e4411",
            leaseSec: null,
            heartbeatAgeSec: 1,
            upSec: 5400,
          }),
        ],
      })}
    />
  ),
}

/**
 * The empty reading, and the decision this screen turns on.
 *
 * The page was open, the worker was in the payload, and a refetch no longer
 * carries it — because somebody force-stopped it, or because the pool scaled
 * to zero on `minIdle: 0`. That is normal here rather than exceptional, so
 * neither a blank screen nor a 404 would be true. The answer is the third one:
 * the container is gone, this is what it was holding, and this is where that
 * work went. The item comes back *requeued* rather than failed, which is
 * exactly why it can still be linked.
 */
export const TornDown: Story = {
  render: () => (
    <Screen
      workerId="wk_2f8a"
      data={board()}
      after={{
        workers: [],
        pools: POOLS,
        items: [item({ status: "queued", claimedBy: null, ageSec: 0 })],
      }}
    />
  ),
}

/**
 * The other empty reading, and a genuinely different one: the query answered
 * and this session never saw the worker at all — an old link, a pasted id, a
 * container from a shift that ended hours ago. It names the missing id rather
 * than saying 404 at somebody, and hands the pool back narrowed to that id so
 * the operator can see for themselves that nothing matches.
 */
export const NotFound: Story = {
  render: () => <Screen workerId="wk_neverwas" data={board({ workers: [] })} />,
}
