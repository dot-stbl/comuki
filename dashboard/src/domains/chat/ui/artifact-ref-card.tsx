import { useMemo, useState } from "react"

import {
  useVisualArtifactsQuery,
  visualArtifactContentUrl,
  type VisualArtifact,
} from "@/domains/artifacts"
import { EvidencePane, EvidenceThumbnail } from "@/shared/ui"
import { env } from "@/shared/config/env"

import styles from "./artifact-ref-card.module.css"

/**
 * The card rendered for a turn's `artifact-ref` part.
 *
 * Three jobs in one component:
 *
 * 1. **Find the artifacts.** The card is handed the project id (the
 *    page knows the scope; the parts do not — see `ArtifactRefPart`'s
 *    header) and the artifact ids the turn referenced. It runs the
 *    project's artifact list and intersects the parts' ids with the
 *    resolved ones. An id that does not intersect is said out loud
 *    rather than dropped — a comment of the conversation may outlive
 *    its bytes — but *what* is said depends on whether the list has
 *    answered yet. See {@link unresolvedWords}.
 * 2. **Render one row per id.** Each id's resolved artifact becomes a
 *    thumbnail button. A document (svg / html) renders as a labelled
 *    tile rather than as document bytes — the operator's mental model
 *    is "what was published", and the modal is the only place that
 *    draws the actual body.
 * 3. **Open the modal.** Click → modal. One modal per card (not per
 *    row), so a card with three thumbnails answers three clicks with
 *    three modals in sequence. The state lives here, not at the page,
 *    because a card on one line has nothing to share with a card on
 *    another line.
 */
export interface ArtifactRefCardProps {
  /** The project the artifacts live under — page-scoped, not part-scoped. */
  projectId: string
  /** Ids the turn referenced. */
  artifactIds: string[]
}

/**
 * Why an id did not become a thumbnail.
 *
 * - `gone` — the list answered and this id is not in it. The publication was
 *   revoked, and the journal kept the pointer.
 * - `unread` — the list could not be read at all. Nothing is known about this
 *   id, including whether it still exists.
 * - `reading` — the request is still in flight.
 */
type UnresolvedState = "gone" | "unread" | "reading"

/**
 * The line under the thumbnails, in the tense the card has actually earned.
 *
 * The card used to say "no longer available" for every id it could not look
 * up — which meant that for as long as the artifact list was in flight, a turn
 * that published three files told the operator all three had been revoked. The
 * count is the same fact in all three cases; the verb is not, and the verb is
 * the part somebody acts on.
 */
function unresolvedWords(count: number, state: UnresolvedState): string {
  const subject =
    count === 1 ? "1 referenced artifact" : `${count} referenced artifacts`
  if (state === "reading") {
    return `${subject}, still loading`
  }
  if (state === "unread") {
    return `${subject} could not be read`
  }
  return count === 1
    ? `${subject} is no longer available`
    : `${subject} are no longer available`
}

export function ArtifactRefCard({
  projectId,
  artifactIds,
}: ArtifactRefCardProps) {
  // One fetch per project per card — `useVisualArtifactsQuery` is cheap
  // and TanStack deduplicates by query key, but a card inside a card
  // and a card inside a card still resolve to the same cache slot.
  const query = useVisualArtifactsQuery(projectId)
  const all = useMemo<VisualArtifact[]>(
    () => query.data?.items ?? [],
    [query.data?.items]
  )

  // Resolve the part's ids against the fetched list. The set is what
  // the card draws; an id that didn't survive is `unresolved`, and what the
  // card is allowed to *say* about it depends on why it did not resolve —
  // which is the whole of `state` below.
  const { resolved, unresolved } = useMemo(() => {
    const lookup = new Map(all.map((entry) => [entry.id, entry]))
    const hit: VisualArtifact[] = []
    const miss: string[] = []
    for (const id of artifactIds) {
      const entry = lookup.get(id)
      if (entry) {
        hit.push(entry)
      } else {
        miss.push(id)
      }
    }
    return { resolved: hit, unresolved: miss }
  }, [all, artifactIds])

  const state: UnresolvedState = query.isSuccess
    ? "gone"
    : query.isError
      ? "unread"
      : "reading"

  const [openArtifact, setOpenArtifact] = useState<VisualArtifact | null>(null)

  if (resolved.length === 0 && unresolved.length === 0) {
    return null
  }

  return (
    <section className={styles.card} data-test="artifact-ref-card">
      {resolved.length > 0 ? (
        <div className={styles.row} data-test="artifact-ref-thumbs">
          {resolved.map((entry) => (
            <EvidenceThumbnail
              key={entry.id}
              size="sm"
              data-test="artifact-ref-thumb"
              src={visualArtifactContentUrl(
                env.apiBaseUrl,
                projectId,
                entry.id
              )}
              contentType={entry.contentType}
              filename={entry.filename}
              title={entry.title ?? entry.filename}
              onActivate={() => setOpenArtifact(entry)}
            />
          ))}
        </div>
      ) : null}
      {unresolved.length > 0 ? (
        <p
          className={styles.orphans}
          data-test="artifact-ref-unresolved"
          data-state={state}
        >
          {unresolvedWords(unresolved.length, state)}
        </p>
      ) : null}
      {openArtifact ? (
        <EvidencePane
          open
          onClose={() => setOpenArtifact(null)}
          src={visualArtifactContentUrl(
            env.apiBaseUrl,
            projectId,
            openArtifact.id
          )}
          contentType={openArtifact.contentType}
          filename={openArtifact.filename}
          title={openArtifact.title ?? openArtifact.filename}
          sizeBytes={openArtifact.sizeBytes}
        />
      ) : null}
    </section>
  )
}
