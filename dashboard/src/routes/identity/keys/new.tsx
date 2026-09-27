import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { CreateKeyPage } from "@/domains/identity"

export const Route = createFileRoute("/identity/keys/new")({
  component: RouteComponent,
})

/* This route renders a form and never a secret. The plaintext exists only in
   `CreateKeyPage`'s own state, above the form, for as long as its dialog is
   open — it is not in this path, not in a search param and not in the router's
   location state, so reloading, bookmarking or sharing this address gives an
   empty form. `key-secret-dialog.tsx` carries the argument. */
function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="identity.manage"
      title={t("route.newApiKey")}
      crumbs={[
        { label: t("crumb.platform") },
        { label: t("crumb.identity"), to: "/identity" },
        { label: t("crumb.newApiKey") },
      ]}
    >
      <CreateKeyPage />
    </RequirePermission>
  )
}
