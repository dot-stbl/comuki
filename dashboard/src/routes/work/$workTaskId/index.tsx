import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { WorkTaskDetailPage } from "@/domains/work"

export const Route = createFileRoute("/work/$workTaskId/")({
  component: RouteComponent,
})

/* The detail screen gates on `work.view`, the same act the list (when it
 * arrives) will gate on. A work task may only be opened by someone who may
 * see the work side of the project, not by guessing the id from a stale link
 * or a leaked run graph.
 *
 * The crumb chain reads `tasks → task_<id>`. The list page at /tasks is the
 * operator's entry point into the work side. The id rides as `task_<id>`
 * because the title it joins to is the host's `WorkTask.title`, not the
 * id; the breadcrumb has to name the id when the title is not yet
 * available, and the literal prefix marks that as the id rather than the
 * title. */
function RouteComponent() {
  const { t } = useTranslation("shell")

  const { workTaskId } = Route.useParams()

  return (
    <RequirePermission
      permission="work.view"
      title={t("route.workTask")}
      crumbs={[
        { label: t("crumb.tasks"), to: "/tasks" },
        { label: `task_${workTaskId}` },
      ]}
    >
      <WorkTaskDetailPage workTaskId={workTaskId} />
    </RequirePermission>
  )
}