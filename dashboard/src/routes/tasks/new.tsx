import { createFileRoute } from "@tanstack/react-router"

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
  return (
    <RequirePermission
      permission="inbox.take"
      title="New task"
      crumbs={[{ label: "tasks", to: "/tasks" }, { label: "new" }]}
    >
      <CreateTaskPage />
    </RequirePermission>
  )
}