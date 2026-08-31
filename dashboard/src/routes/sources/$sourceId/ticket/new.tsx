import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { CreateTicketPage } from "@/domains/sources"

export const Route = createFileRoute("/sources/$sourceId/ticket/new")({
  component: RouteComponent,
})

/* `sources.view` opens the door and `inbox.take` opens the act, and the two are
   deliberately different people: writing a bug down is a member's act, while
   configuring the connection it lands in is an administrator's. Gating the
   route on `inbox.take` would be the tidier-looking choice and the wrong one —
   the page is reached from a source's own page, and somebody who may read this
   section should meet a form that explains what it needs rather than a
   forbidden screen that names a permission they were never asking about.

   The crumb path names the connection, so the way back agrees with the way in.
   The route cannot spell that crumb — it has an id and not a name — so the page
   fills it once the connection has loaded. */
function RouteComponent() {
  const { sourceId } = Route.useParams()

  return (
    <RequirePermission
      permission="sources.view"
      title="New ticket"
      crumbs={[
        { label: "configure" },
        { label: "sources", to: "/sources" },
        { label: "new ticket" },
      ]}
    >
      <CreateTicketPage sourceId={sourceId} />
    </RequirePermission>
  )
}
