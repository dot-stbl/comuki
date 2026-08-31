import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { ComputePage } from "@/domains/compute"

export const Route = createFileRoute("/compute")({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <RequirePermission
      permission="compute.view"
      title="Compute"
      crumbs={[{ label: "platform" }, { label: "compute" }]}
    >
      <ComputePage />
    </RequirePermission>
  )
}
