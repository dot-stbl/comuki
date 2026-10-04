import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { ProceduresPage } from "@/domains/procedures"

/**
 * Procedure workbench route — the page reads the workbench mode from
 * a URL search param (`?mode=studio|live|replay`); the route param
 * `procedureKey` names the procedure. One route, three panels (the
 * decision is in `domains/procedures/AGENTS.md` — single page with
 * mode from URL keeps the chrome shared and the nav uncluttered).
 *
 * The procedure-permission gate is `procedures.view`; a viewer-only
 * operator sees the published procedure, never an unpublished one.
 */
export const Route = createFileRoute("/procedures/$procedureKey")({
  component: RouteComponent,
})

function RouteComponent() {
  const { t } = useTranslation("shell")
  const { procedureKey } = Route.useParams()
  return (
    <RequirePermission
      permission="procedures.view"
      title={t("route.procedure")}
      crumbs={[
        { label: t("crumb.procedures"), to: "/procedures" },
        { label: procedureKey },
      ]}
    >
      <ProceduresPage />
    </RequirePermission>
  )
}