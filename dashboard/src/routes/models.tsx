import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { ModelsPage } from "@/domains/models"

export const Route = createFileRoute("/models")({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <RequirePermission
      permission="models.view"
      title="Models"
      crumbs={[{ label: "platform" }, { label: "models" }]}
    >
      <ModelsPage />
    </RequirePermission>
  )
}
