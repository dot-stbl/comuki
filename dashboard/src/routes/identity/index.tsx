import { createFileRoute, useNavigate } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { IdentityPage, isIdentityTab, type IdentityTab } from "@/domains/identity"

export interface IdentitySearch {
  /**
   * Which of the three lists is showing. In the URL rather than in component
   * state because every act on this screen leaves for a form page and comes
   * back: a tab held locally would drop the operator on `users` after they had
   * just written a key.
   *
   * Optional, and that is load-bearing rather than lazy: `/identity` is a rail
   * destination and an account-menu destination, and making the parameter
   * required would force every one of those call sites to name a tab in order
   * to link to the section at all. Absent means the first list.
   */
  tab?: IdentityTab
  /**
   * A subject to narrow the showing list to — what a form hands over once it
   * has written something. It seeds that panel's own text filter, so the
   * narrowing is visible in the toolbar and clears in one click.
   */
  q?: string
}

export const Route = createFileRoute("/identity/")({
  validateSearch: (search: Record<string, unknown>): IdentitySearch => {
    const parsed: IdentitySearch = {}
    if (isIdentityTab(search.tab)) {
      parsed.tab = search.tab
    }
    const q = typeof search.q === "string" ? search.q.trim() : ""
    if (q) {
      parsed.q = q
    }
    return parsed
  },
  component: RouteComponent,
})

function RouteComponent() {
  const { tab = "users", q } = Route.useSearch()
  const navigate = useNavigate()

  return (
    <RequirePermission
      permission="identity.manage"
      title="Identity"
      crumbs={[{ label: "platform" }, { label: "identity" }]}
    >
      <IdentityPage
        tab={tab}
        focus={q}
        onTabChange={(next) => {
          // `q` is dropped on purpose: it was written for one list, and
          // carrying it across would silently narrow a list the operator
          // switched to in order to see all of it. `replace` because moving
          // between tabs is not a step worth pressing back through.
          void navigate({ to: "/identity", search: { tab: next }, replace: true })
        }}
      />
    </RequirePermission>
  )
}
