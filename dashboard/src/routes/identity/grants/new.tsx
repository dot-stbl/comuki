import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { GrantRolePage } from "@/domains/identity"

export const Route = createFileRoute("/identity/grants/new")({
  component: RouteComponent,
})

function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="identity.manage"
      title={t("route.grantRole")}
      crumbs={[
        { label: t("crumb.platform") },
        { label: t("crumb.identity"), to: "/identity" },
        { label: t("crumb.grantRole") },
      ]}
    >
      <GrantRolePage />
    </RequirePermission>
  )
}
