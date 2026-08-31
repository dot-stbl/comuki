import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { VerifyPage } from "@/domains/verify"

export const Route = createFileRoute("/verify")({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <RequirePermission
      permission="verify.view"
      title="Verify"
      crumbs={[{ label: "configure" }, { label: "verify" }]}
    >
      <VerifyPage />
    </RequirePermission>
  )
}
