import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { InviteUserPage } from "@/domains/identity"

export const Route = createFileRoute("/identity/users/new")({
  component: RouteComponent,
})

/* Identity is one act end to end: `identity.manage` opens the lists and every
   form under them, so this gates on the same permission the section does. */
function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="identity.manage"
      title={t("route.newUser")}
      crumbs={[
        { label: t("crumb.platform") },
        { label: t("crumb.identity"), to: "/identity" },
        { label: t("crumb.newUser") },
      ]}
    >
      <InviteUserPage />
    </RequirePermission>
  )
}
