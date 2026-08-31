import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { RunDetailPage } from "@/domains/runs"

export const Route = createFileRoute("/runs/$runId")({
  component: RouteComponent,
})

/* The detail screen gates on the same act as the list: `/runs/*` is one
   section, and a run you may not see in a list is not a run you may open by
   guessing its id. */
function RouteComponent() {
  return (
    <RequirePermission
      permission="runs.view"
      title="Run"
      crumbs={[{ label: "live runs", to: "/runs" }, { label: "run" }]}
    >
      <RunDetailPage />
    </RequirePermission>
  )
}
