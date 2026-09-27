import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { CreateProjectPage } from "@/domains/projects"

export const Route = createFileRoute("/projects/new")({
  component: RouteComponent,
})

/* The route gates on the act it performs, not on the section it sits in:
   `projects.view` opens the registry and `projects.create` opens this, so a
   viewer who guesses the URL meets the forbidden state with the roles that
   would work written on it — rather than a form whose only submit refuses. */
function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="projects.create"
      title={t("route.newProject")}
      crumbs={[
        { label: t("crumb.platform") },
        { label: t("crumb.projects"), to: "/projects" },
        { label: t("crumb.new") },
      ]}
    >
      <CreateProjectPage />
    </RequirePermission>
  )
}
