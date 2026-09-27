import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { CreateTaskPage } from "@/domains/tasks"

export const Route = createFileRoute("/tasks/new")({
  component: RouteComponent,
})

/* The route gates on the act it performs: `inbox.view` opens the backlog and
   `inbox.take` opens this, so a viewer who guesses the URL meets the forbidden
   state with the roles that would work written on it — rather than a form
   whose only submit refuses. */
function RouteComponent() {
  const { t } = useTranslation("shell")

  return (
    <RequirePermission
      permission="inbox.take"
      title={t("route.newTask")}
      crumbs={[
        { label: t("crumb.tasks"), to: "/tasks" },
        { label: t("crumb.new") },
      ]}
    >
      <CreateTaskPage />
    </RequirePermission>
  )
}
