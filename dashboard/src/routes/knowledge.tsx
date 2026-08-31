import { createFileRoute, useNavigate } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import {
  KnowledgePage,
  isKnowledgeTab,
  type KnowledgeTab,
} from "@/domains/knowledge"

export interface KnowledgeSearch {
  /**
   * Which of the two sections is showing. In the URL rather than in component
   * state because a section is a thing one operator sends another — "the gate
   * for plexor is here" is a link, not a set of directions — and because the
   * screen's own query (`q`) belongs to the library alone, so switching
   * sections must be able to drop it without remounting anything.
   *
   * Optional, and that is load-bearing rather than lazy: `/knowledge` is a
   * rail destination, and making the parameter required would force every one
   * of those call sites to name a section in order to link to the screen at
   * all. Absent means the first section. An unknown value off the URL means
   * the first section too, for the same reason the screen itself falls back:
   * `?tab=gate` in the hands of a session that cannot see the gate is a stale
   * link, not an error worth showing.
   */
  tab?: KnowledgeTab
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
    const parsed: KnowledgeSearch = {}
    if (isKnowledgeTab(search.tab)) {
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
  const search = Route.useSearch()

  /* A match's search is the URL's raw values merged over what
     `validateSearch` kept, so an unknown word off the address bar reaches
     this component as-is — `?tab=nonsense` arrives with `tab` set, not
     absent. Asked again here, the same question `validateSearch` asked, so
     the page is never handed a section it cannot show; `q` is checked for
     the same reason, since a raw value can be anything at all. */
  const tab = isKnowledgeTab(search.tab) ? search.tab : "library"
  const q = typeof search.q === "string" ? search.q : undefined
  const navigate = useNavigate()

  return (
    <RequirePermission
      permission="knowledge.view"
      title="Knowledge"
      crumbs={[{ label: "configure" }, { label: "knowledge" }]}
    >
      <KnowledgePage
        tab={tab}
        focus={q}
        onTabChange={(next) => {
          // `q` is dropped on purpose: it was written for the library's list,
          // and carrying it across would silently narrow a section the
          // operator switched to in order to see all of. `replace` because
          // moving between sections is not a step worth pressing back
          // through — the back button should leave the screen.
          void navigate({
            to: "/knowledge",
            search: { tab: next },
            replace: true,
          })
        }}
      />
    </RequirePermission>
  )
}
