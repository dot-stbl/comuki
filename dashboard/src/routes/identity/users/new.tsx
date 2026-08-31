import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { InviteUserPage } from "@/domains/identity"

export const Route = createFileRoute("/identity/users/new")({
  component: RouteComponent,
})

/* Identity is one act end to end: `identity.manage` opens the lists and every
   form under them, so this gates on the same permission the section does. */
function RouteComponent() {
  return (
    <RequirePermission
      permission="identity.manage"
      title="New user"
      crumbs={[
        { label: "platform" },
        { label: "identity", to: "/identity" },
        { label: "new user" },
      ]}
    >
      <InviteUserPage />
    </RequirePermission>
  )
}
