import { createFileRoute, useNavigate } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { QueuePage } from "@/domains/queue"

export interface QueueSearch {
  /**
   * What the *queue* half is narrowed to — its toolbar's promoted search
   * filter, held in the URL rather than in component state.
   *
   * `q`, spelled the same as `/runs`, `/projects` and `/identity` spell it. It
   * matches a work item, the run it belongs to, its step and its profile.
   */
  q?: string
  /**
   * What the *pool* half is narrowed to. A parameter of its own, because the
   * two halves of this screen are narrowed independently on purpose and they
   * answer to different strings: a worker id, a provider handle and an image
   * digest here, a work item and a run there. One value serving both would
   * empty whichever half it was not written for.
   *
   * It is also what makes an image digest resolvable at all — pasting
   * `sha256:9c41ab` lands on the containers running it, which is the question
   * a digest is usually being asked in aid of.
   */
  w?: string
}

/* An *index* route rather than a flat `queue.tsx`, and the reason is the
   generator rather than a preference. A worker has a page of its own now at
   `/queue/workers/<id>`, and TanStack's file-based generator would turn a flat
   `queue.tsx` into a **layout** route for that child — the pool board would
   then render above the worker's own screen, which is not a nesting either
   page was written for. `/runs` already sits in exactly this shape for exactly
   this reason: `runs/index.tsx` beside `runs/$runId.tsx`.

   Nothing else about the route moved, and nothing else needed to. `/queue` is
   still the address — an index route answers to its parent's path — so every
   link, crumb and shape href in the product still spells it the way it always
   did, including the two `shapes.ts` mints for a work item and a digest. */
export const Route = createFileRoute("/queue/")({
  validateSearch: (search: Record<string, unknown>): QueueSearch => {
    const parsed: QueueSearch = {}
    const q = typeof search.q === "string" ? search.q.trim() : ""
    if (q) {
      parsed.q = q
    }
    const w = typeof search.w === "string" ? search.w.trim() : ""
    if (w) {
      parsed.w = w
    }
    return parsed
  },
  component: RouteComponent,
})

function RouteComponent() {
  const { q, w } = Route.useSearch()
  const navigate = useNavigate()

  /* One writer for both halves, so narrowing one never drops the other: the
     operator who filtered the pool to a draining container and then went
     looking for what it was holding must not lose the first filter to the
     second. `replace`, because typing is not a sequence of places to walk back
     through. */
  const write = (next: QueueSearch) => {
    void navigate({ to: "/queue", search: next, replace: true })
  }

  return (
    <RequirePermission
      permission="queue.view"
      title="Queue & workers"
      crumbs={[{ label: "observe", to: "/runs" }, { label: "queue" }]}
    >
      <QueuePage
        search={q}
        onSearchChange={(next) => {
          write({ ...(next.trim() ? { q: next } : {}), ...(w ? { w } : {}) })
        }}
        workerSearch={w}
        onWorkerSearchChange={(next) => {
          write({ ...(q ? { q } : {}), ...(next.trim() ? { w: next } : {}) })
        }}
      />
    </RequirePermission>
  )
}
