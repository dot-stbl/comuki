import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { ProjectsPage } from "@/domains/projects"

export interface ProjectsSearch {
  /**
   * A handle to bring to the top of the list — what `/projects/new` hands over
   * once it has created something. It seeds the toolbar's own text filter, so
   * the operator can see why the list is short and clear it in one click.
   */
  q?: string
}

export const Route = createFileRoute("/projects/")({
  validateSearch: (search: Record<string, unknown>): ProjectsSearch => {
    const q = typeof search.q === "string" ? search.q.trim() : ""
    return q ? { q } : {}
  },
  component: RouteComponent,
})

function RouteComponent() {
  const { q } = Route.useSearch()

  return (
    <RequirePermission
      permission="projects.view"
      title="Projects"
      crumbs={[{ label: "platform" }, { label: "projects" }]}
    >
      <ProjectsPage focus={q} />
    </RequirePermission>
  )
}
