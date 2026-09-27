import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { ApprovalsPage } from "@/domains/approvals"

export const Route = createFileRoute("/approvals")({
  component: RouteComponent,
})

function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="plans.approve"
      title={t("route.approvals")}
      crumbs={[
        { label: t("crumb.observe"), to: "/runs" },
        { label: t("crumb.approvals") },
      ]}
    >
      <ApprovalsPage />
    </RequirePermission>
  )
}
