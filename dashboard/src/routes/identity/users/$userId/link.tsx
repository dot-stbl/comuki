import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { LinkOidcPage } from "@/domains/identity"

export const Route = createFileRoute("/identity/users/$userId/link")({
  component: RouteComponent,
})

/* The one flow in this section that edits something that already exists, so
   it is the one whose address names a subject. The page answers for an id that
   no longer resolves — a stale tab is the ordinary way to arrive with one.

   These crumbs are only ever seen by a shift that may not administer identity,
   because `RequirePermission` renders them in place of the page. They still
   run through the person, since the person's page is this one's parent — but
   they name the crumb `person` rather than an address: nothing has been
   fetched at this point, and there is nothing here but an id. */
function RouteComponent() {
  const { userId } = Route.useParams()

  return (
    <RequirePermission
      permission="identity.manage"
      title="Link an oidc subject"
      crumbs={[
        { label: "platform" },
        { label: "identity", to: "/identity" },
        { label: "person", to: `/identity/users/${userId}` },
        { label: "link an oidc subject" },
      ]}
    >
      <LinkOidcPage userId={userId} />
    </RequirePermission>
  )
}
