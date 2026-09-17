import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { RunDetailPage } from "@/domains/runs"

export const Route = createFileRoute("/runs/$runId")({
  component: RouteComponent,
})

/* The detail screen gates on the same act as the list: `/runs/*` is one
   section, and a run you may not see in a list is not a run you may open by
   guessing its id.

   The param is read here and handed down as a prop rather than pulled off
   `getRouteApi` inside the page — the same split the other four detail routes
   already use (`$projectId`, `$sourceId`, `$userId`, `$workerId`). The id is
   the only thing the screen wants from the router, and taking it as a value is
   what lets the screen be mounted in a story and in a test without the
   generated route tree standing behind it. The route knows about routing, the
   page knows about a run. */
function RouteComponent() {
  const { runId } = Route.useParams()

  return (
    <RequirePermission
      permission="runs.view"
      title="Run"
      crumbs={[{ label: "live runs", to: "/runs" }, { label: "run" }]}
    >
      <RunDetailPage runId={runId} />
    </RequirePermission>
  )
}
