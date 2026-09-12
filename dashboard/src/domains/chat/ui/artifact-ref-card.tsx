import { useMemo, useState } from "react"

import {
  isImageMime,
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
 *    resolved ones. Empty intersection means the operator should see
 *    "referenced but gone" rather than nothing — a comment of the
 *    conversation may outlive its bytes.
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
    [query.data?.items],
  )

  // Resolve the part's ids against the fetched list. The set is what
  // the card draws; an id that didn't survive (the referenced
  // artifact was deleted, the journal still has the pointer) is the
  // `orphans` list — surfaced separately so the operator can read
  // that a turn's publication has since been revoked.
  const { resolved, orphans } = useMemo(() => {
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
    return { resolved: hit, orphans: miss }
  }, [all, artifactIds])

  // Image-only at the card level — the modal handles documents. A
  // card can carry documents too, but the thumbnail is still a
  // labelled tile (see `EvidenceThumbnail`); the modal does the mime
  // dispatch.
  void isImageMime

  const [openArtifact, setOpenArtifact] = useState<VisualArtifact | null>(null)

  if (resolved.length === 0 && orphans.length === 0) {
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
                entry.id,
              )}
              contentType={entry.contentType}
              filename={entry.filename}
              title={entry.title ?? entry.filename}
              onActivate={() => setOpenArtifact(entry)}
            />
          ))}
        </div>
      ) : null}
      {orphans.length > 0 ? (
        <p className={styles.orphans}>
          {orphans.length === 1
            ? "1 referenced artifact is no longer available"
            : `${orphans.length} referenced artifacts are no longer available`}
        </p>
      ) : null}
      {openArtifact ? (
        <EvidencePane
          open
          onClose={() => setOpenArtifact(null)}
          src={visualArtifactContentUrl(
            env.apiBaseUrl,
            projectId,
            openArtifact.id,
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
