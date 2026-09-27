import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { HomePage } from "@/domains/home"

export const Route = createFileRoute("/")({
  component: RouteComponent,
})

function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="runs.view"
      title={t("route.attention")}
      crumbs={[{ label: t("crumb.attention") }]}
    >
      <HomePage />
    </RequirePermission>
  )
}
