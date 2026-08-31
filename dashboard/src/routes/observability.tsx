import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { ObservabilityPage } from "@/domains/observability"

export const Route = createFileRoute("/observability")({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <RequirePermission
      permission="observability.view"
      title="Observability"
      crumbs={[{ label: "platform" }, { label: "observability" }]}
    >
      <ObservabilityPage />
    </RequirePermission>
  )
}
