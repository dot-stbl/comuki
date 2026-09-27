import { createFileRoute } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { RequirePermission } from "@/app/layout/require-permission"
import { TasksPage } from "@/domains/tasks"

export interface TasksSearch {
  /**
   * A query to narrow the backlog to on arrival.
   *
   * In the URL for the same two reasons `projects` and `identity` put theirs
   * there: a global search has to be able to hand a query off to the screen
   * that can answer it, and a narrowed list has to be pasteable into a ticket.
   * It seeds the toolbar's own text filter, so the operator can see why the
   * list is short and clear it in one click.
   */
  q?: string
}

export const Route = createFileRoute("/tasks/")({
  validateSearch: (search: Record<string, unknown>): TasksSearch => {
    const q = typeof search.q === "string" ? search.q.trim() : ""
    return q ? { q } : {}
  },
  component: RouteComponent,
})

function RouteComponent() {
  const { t } = useTranslation("shell")

  const { q } = Route.useSearch()

  return (
    <RequirePermission
      permission="inbox.view"
      title={t("route.tasks")}
      crumbs={[{ label: t("crumb.tasks") }]}
    >
      <TasksPage focus={q} />
    </RequirePermission>
  )
}
