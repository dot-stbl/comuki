import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { SourcesPage } from "@/domains/sources"

export interface SourcesSearch {
  /**
   * What the connections list is narrowed to — the toolbar's promoted search
   * filter, held in the URL rather than in component state.
   *
   * Two things fall out of that, and both are worth the parameter. A narrowed
   * list becomes a *link*: the operator who found the two connections that
   * matter can paste the address into a ticket instead of describing which
   * boxes to type into. And a reload stops throwing the filter away.
   *
   * It is also what another screen hands work off to. A project's own page
   * sends its sources here as `/sources?q=<project slug>`, and the global
   * palette resolves a project handle to the same address — which is only
   * honest because the list's promoted filter matches the **project key** as
   * well as the fields its placeholder names. A destination that cannot receive
   * what it is sent lands the operator on an empty screen, which is worse than
   * not resolving at all; see the contract at the top of `app/search/shapes.ts`.
   *
   * `q`, spelled the same as `/runs`, `/projects`, `/tasks` and `/identity`
   * already spell it. Absent means unfiltered; the empty string is never
   * written.
   */
  q?: string
}

/**
 * `/sources/` and not `/sources`, which is required rather than cosmetic: a
 * `sources.tsx` beside a `sources/` folder makes TanStack's generator treat the
 * file as a *layout* route wrapping the children, and the list would then
 * render underneath `/sources/new`. `/runs` and `/projects` are spelled this
 * way for the same reason.
 */
export const Route = createFileRoute("/sources/")({
  validateSearch: (search: Record<string, unknown>): SourcesSearch => {
    const q = typeof search.q === "string" ? search.q.trim() : ""
    return q ? { q } : {}
  },
  component: RouteComponent,
})

function RouteComponent() {
  const { q } = Route.useSearch()

  return (
    <RequirePermission
      permission="sources.view"
      title="Sources"
      crumbs={[{ label: "configure" }, { label: "sources" }]}
    >
      <SourcesPage focus={q} />
    </RequirePermission>
  )
}
