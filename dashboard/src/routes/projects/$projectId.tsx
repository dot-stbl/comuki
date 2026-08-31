import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { ProjectDetailPage } from "@/domains/projects"

export const Route = createFileRoute("/projects/$projectId")({
  component: RouteComponent,
})

/* The detail screen gates on the same act as the registry: `/projects/*` is
   one section, and a project you may not see in a list is not a project you
   may open by guessing its id. `projects.view` is a *platform* permission —
   asked with no project id, because being project-admin of three projects must
   never open the platform's registry.

   The param is read here and handed down as a prop rather than pulled off
   `getRouteApi` inside the page: the id is the only thing the screen wants
   from the router, and taking it as a value is what lets the screen be mounted
   in a story and in a test without the generated route tree standing behind
   it. `LinkOidcPage` is the precedent. */
function RouteComponent() {
  const { projectId } = Route.useParams()

  return (
    <RequirePermission
      permission="projects.view"
      title="Project"
      crumbs={[
        { label: "platform" },
        { label: "projects", to: "/projects" },
        { label: "project" },
      ]}
    >
      <ProjectDetailPage projectId={projectId} />
    </RequirePermission>
  )
}
