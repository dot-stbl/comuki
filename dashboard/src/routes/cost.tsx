import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { CostPage } from "@/domains/cost"

export const Route = createFileRoute("/cost")({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <RequirePermission
      permission="cost.view"
      title="Cost & failures"
      crumbs={[{ label: "observe", to: "/runs" }, { label: "cost" }]}
    >
      <CostPage />
    </RequirePermission>
  )
}
