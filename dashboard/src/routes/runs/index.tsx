import { createFileRoute, useNavigate } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { RunsPage } from "@/domains/runs"

export interface RunsSearch {
  /**
   * What the list is narrowed to — the toolbar's promoted search filter, held
   * in the URL rather than in component state.
   *
   * Two things fall out of that, and both are worth the parameter. A narrowed
   * list becomes a *link*: the duty engineer who found the three runs that
   * matter can paste the address into a ticket instead of describing which
   * boxes to type into. And a reload stops throwing the filter away, which is
   * the difference between a screen you can leave open all shift and one you
   * have to set up again every time it is refreshed.
   *
   * It is also what the global palette hands free text off to. The palette
   * refuses to invent a result list it cannot back, so instead it offers
   * "search live runs for «webhook»" — and that offer is only honest because
   * the filter has an address to be sent to.
   *
   * `q`, spelled the same as `/projects` and `/identity` already spell it.
   * Absent means unfiltered; the empty string is never written.
   */
  q?: string
}

export const Route = createFileRoute("/runs/")({
  validateSearch: (search: Record<string, unknown>): RunsSearch => {
    const q = typeof search.q === "string" ? search.q.trim() : ""
    return q ? { q } : {}
  },
  component: RouteComponent,
})

function RouteComponent() {
  const { q } = Route.useSearch()
  const navigate = useNavigate()

  return (
    <RequirePermission
      permission="runs.view"
      title="Live runs"
      crumbs={[{ label: "live runs" }]}
    >
      <RunsPage
        search={q}
        onSearchChange={(next) => {
          /* `replace`, because typing is not a sequence of places the operator
             wants to walk back through: a filter typed one character at a time
             would otherwise leave eight entries in the history and eight
             presses of back between here and wherever they came from. */
          void navigate({
            to: "/runs",
            search: next.trim() ? { q: next } : {},
            replace: true,
          })
        }}
      />
    </RequirePermission>
  )
}
