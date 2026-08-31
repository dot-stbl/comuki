import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { ApprovalsPage } from "@/domains/approvals"

export const Route = createFileRoute("/approvals")({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <RequirePermission
      permission="plans.approve"
      title="Approvals"
      crumbs={[{ label: "observe", to: "/runs" }, { label: "approvals" }]}
    >
      <ApprovalsPage />
    </RequirePermission>
  )
}
