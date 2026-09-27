import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { CostPage } from "@/domains/cost"

export const Route = createFileRoute("/cost")({
  component: RouteComponent,
})

function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="cost.view"
      title={t("route.cost")}
      crumbs={[
        { label: t("crumb.observe"), to: "/runs" },
        { label: t("crumb.cost") },
      ]}
    >
      <CostPage />
    </RequirePermission>
  )
}
