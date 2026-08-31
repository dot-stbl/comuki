import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { UserDetailPage } from "@/domains/identity"

export const Route = createFileRoute("/identity/users/$userId/")({
  component: RouteComponent,
})

/* One person, and the parent of the link flow beneath it.
 *
 * It gates on the same act as the list: `/identity/*` is one section, and a
 * person you may not see in a list is not a person you may open by guessing an
 * id. The route reads the param and hands it down as a prop rather than the
 * page reaching for the route itself — the same shape the link page uses, and
 * the reason both can be mounted in a story and in a test without the
 * generated route tree. The page answers for an id that no longer resolves; a
 * stale tab is the ordinary way to arrive with one. */
function RouteComponent() {
  const { userId } = Route.useParams()

  return (
    <RequirePermission
      permission="identity.manage"
      title="Person"
      crumbs={[
        { label: "platform" },
        { label: "identity", to: "/identity" },
        { label: "person" },
      ]}
    >
      <UserDetailPage userId={userId} />
    </RequirePermission>
  )
}
