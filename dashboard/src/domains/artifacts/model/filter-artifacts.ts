import type { VisualArtifact } from "./types"

/**
 * Reading the list.
 *
 * Two reads happen against the same page shape:
 *
 * 1. **Run evidence** — `evidenceForRun(items, runId)`: keep rows whose
 *    `runId` matches, oldest first so the strip paints left-to-right in
 *    publish order.
 * 2. **Ticket thumbnail** — `latestPngForTicket(items, ticketId)`: the
 *    newest png linked to the ticket. The list is fetched once per row's
 *    project and cached by `useArtifactsVisualList`; this filter is the
 *    only thing the inbox cell does not share with the run.
 *
 * Both filters stay defensive — a row whose `runId` / `ticketId` somehow
 * matches `null` is dropped — because the wire carries `null` as
 * "unlinked" (a brain mockup in chat) and the dashboard's only renders
 * are by link.
 */
export function evidenceForRun(
  items: readonly VisualArtifact[],
  runId: string,
): VisualArtifact[] {
  return items.filter(
    (entry) => entry.runId !== null && entry.runId === runId,
  )
}

/**
 * Latest png linked to a ticket.
 *
 * `latestPngForTicket` returns the **newest** png by `createdAt`, or
 * `null` when none match. Newer over older because the inbox is a watch
 * surface — the cell is meant to show the most recent screenshot — and
 * showing the oldest would invite an operator to mistake "what was" for
 * "what is". The list's `createdAt` is the wire's ISO-8601 string, which
 * `Date.parse` understands as UTC; safe to compare lexically (the ISO
 * format is constant-width and sort-stable).
 */
export function latestPngForTicket(
  items: readonly VisualArtifact[],
  ticketId: string,
): VisualArtifact | null {
  let newest: VisualArtifact | null = null
  for (const entry of items) {
    if (
      entry.ticketId !== null &&
      entry.ticketId === ticketId &&
      entry.contentType.toLowerCase() === "image/png"
    ) {
      if (newest === null || entry.createdAt > newest.createdAt) {
        newest = entry
      }
    }
  }
  return newest
}
