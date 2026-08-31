import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { SourceDetailPage } from "@/domains/sources"

export const Route = createFileRoute("/sources/$sourceId/")({
  component: RouteComponent,
})

/* The detail screen gates on the same act as the list: `/sources/*` is one
   section, and a source you may not see in a list is not a source you may open
   by guessing its id. What is *edited* on it is `sources.edit`, which is a
   project permission and therefore cannot be asked here — the page asks it
   against this connection's own project, once the connection is known.

   The page also answers for an id that no longer resolves. Disconnecting is
   something this section does, so a stale tab is the ordinary way to arrive
   with one rather than an error worth a route guard. */
function RouteComponent() {
  const { sourceId } = Route.useParams()

  return (
    <RequirePermission
      permission="sources.view"
      title="Source"
      crumbs={[
        { label: "configure" },
        { label: "sources", to: "/sources" },
        { label: "source" },
      ]}
    >
      <SourceDetailPage sourceId={sourceId} />
    </RequirePermission>
  )
}
