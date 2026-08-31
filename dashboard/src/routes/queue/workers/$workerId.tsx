import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { WorkerDetailPage } from "@/domains/queue"

export const Route = createFileRoute("/queue/workers/$workerId")({
  component: RouteComponent,
})

/* One container, gated on the same act as the board it was opened from.
 * `/queue/*` is one section: a pool you may not see in a list is not a pool
 * you may read one row of by guessing a worker id — the same argument
 * `/runs/$runId` makes for a run.
 *
 * The param is read here and handed down as a prop rather than pulled out of
 * `getRouteApi` inside the page. That is the split `identity/users/$userId`
 * already uses, and it is what lets the page be mounted in a story or a test
 * without standing up the product's whole route tree first: the route knows
 * about routing, the page knows about a worker.
 */
function RouteComponent() {
  const { workerId } = Route.useParams()

  return (
    <RequirePermission
      permission="queue.view"
      title="Worker"
      crumbs={[
        { label: "observe", to: "/runs" },
        { label: "queue", to: "/queue" },
        { label: "worker" },
      ]}
    >
      <WorkerDetailPage workerId={workerId} />
    </RequirePermission>
  )
}
