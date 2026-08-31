import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { HomePage } from "@/domains/home"

export const Route = createFileRoute("/")({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <RequirePermission
      permission="runs.view"
      title="Attention"
      crumbs={[{ label: "attention" }]}
    >
      <HomePage />
    </RequirePermission>
  )
}
