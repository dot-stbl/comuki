import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { KnowledgePage } from "@/domains/knowledge"

export interface KnowledgeSearch {
  /**
   * A query to narrow the rule set to on arrival.
   *
   * It seeds the screen's own search field, so the narrowing is visible in the
   * band the operator would clear it from rather than being applied invisibly.
   * In the URL because that is what makes a narrowed rule set a thing you can
   * paste into a review — "the two rules this argument is about" is a link, not
   * an instruction to go and type something.
   */
  q?: string
}

export const Route = createFileRoute("/knowledge")({
  validateSearch: (search: Record<string, unknown>): KnowledgeSearch => {
    const q = typeof search.q === "string" ? search.q.trim() : ""
    return q ? { q } : {}
  },
  component: RouteComponent,
})

function RouteComponent() {
  const { q } = Route.useSearch()

  return (
    <RequirePermission
      permission="knowledge.view"
      title="Knowledge"
      crumbs={[{ label: "configure" }, { label: "knowledge" }]}
    >
      <KnowledgePage focus={q} />
    </RequirePermission>
  )
}
