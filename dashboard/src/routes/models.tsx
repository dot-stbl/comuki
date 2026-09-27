import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { ModelsPage } from "@/domains/models"

export const Route = createFileRoute("/models")({
  component: RouteComponent,
})

function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="models.view"
      title={t("route.models")}
      crumbs={[{ label: t("crumb.platform") }, { label: t("crumb.models") }]}
    >
      <ModelsPage />
    </RequirePermission>
  )
}
