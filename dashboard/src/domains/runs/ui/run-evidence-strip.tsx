import {
  EvidenceViewer,
  evidenceForRun,
  useVisualArtifactsQuery,
  type VisualArtifact,
} from "@/domains/artifacts"
import { env } from "@/shared/config/env"

import styles from "./run-evidence-strip.module.css"

/**
 * The evidence strip next to the pr-report.
 *
 * Lives in `runs/` rather than `artifacts/` because the strip is shaped by
 * the run page — a labelled region, placed under the brief, that vanishes
 * when the run has nothing to show. The artifact domain owns the
 * data primitives; this file is the run-shaped wrapper.
 *
 * Two order arguments:
 *
 * 1. **Filter on the server, not here.** The list call carries the
 *    `runId` filter, so the EF query (when slice 1's placeholder gives
 *    way to the real one) does the narrowing once. The helper
 *    `evidenceForRun` is a defensive client-side filter for the mock
 *    seed — same in spirit, far cheaper on the wire.
 * 2. **Render empty when empty.** The pr-report sits beside this strip;
 *    the strip's absence is itself an answer ("no screenshots yet"), not
 *    a missing piece of UI.
 */
export interface RunEvidenceStripProps {
  projectId: string
  runId: string
  /** Pre-fetched items, when the page already has the project list handy. */
  items?: readonly VisualArtifact[]
}

export function RunEvidenceStrip({
  projectId,
  runId,
  items,
}: RunEvidenceStripProps) {
  const query = useVisualArtifactsQuery(projectId, { runId })
  const effective = items ?? query.data?.items ?? []
  // Defence-in-depth: when items come from a server that hasn't yet
  // scoped the query by runId, the helper narrows them again. Cheap on
  // a list this size.
  const filtered = evidenceForRun(effective, runId)

  if (filtered.length === 0) {
    return null
  }

  return (
    <section
      className={styles.strip}
      aria-labelledby={`run-${runId}-evidence`}
      data-test="run-evidence-strip"
    >
      <h3 id={`run-${runId}-evidence`} className={styles.title}>
        evidence
      </h3>
      <EvidenceViewer
        projectId={projectId}
        baseUrl={env.apiBaseUrl}
        items={filtered}
        emptyLabel="no screenshots published yet"
        size="md"
        variant="strip"
      />
    </section>
  )
}
