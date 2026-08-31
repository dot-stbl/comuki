import { createFileRoute } from "@tanstack/react-router"

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
  return (
    <RequirePermission
      permission="projects.create"
      title="New project"
      crumbs={[
        { label: "platform" },
        { label: "projects", to: "/projects" },
        { label: "new" },
      ]}
    >
      <CreateProjectPage />
    </RequirePermission>
  )
}
