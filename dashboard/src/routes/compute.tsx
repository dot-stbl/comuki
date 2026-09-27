import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { ComputePage } from "@/domains/compute"

export const Route = createFileRoute("/compute")({
  component: RouteComponent,
})

function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="compute.view"
      title={t("route.compute")}
      crumbs={[{ label: t("crumb.platform") }, { label: t("crumb.compute") }]}
    >
      <ComputePage />
    </RequirePermission>
  )
}
