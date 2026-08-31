import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { GrantRolePage } from "@/domains/identity"

export const Route = createFileRoute("/identity/grants/new")({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <RequirePermission
      permission="identity.manage"
      title="Grant a role"
      crumbs={[
        { label: "platform" },
        { label: "identity", to: "/identity" },
        { label: "grant a role" },
      ]}
    >
      <GrantRolePage />
    </RequirePermission>
  )
}
