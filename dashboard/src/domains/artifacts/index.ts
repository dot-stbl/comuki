/**
 * Public surface of the visual-artifacts domain.
 *
 * One domain because visual artifacts are cross-cutting — the run page
 * reads them by `runId`, the inbox reads them by `ticketId`, the chat
 * reads them by id. Putting the model + queries in any one of those
 * three domains would couple the others' pages to a foreign domain's
 * `api/`, and the right split is one domain that the three surfaces
 * pull from.
 *
 * Surfaced through the index barrel so callers write
 * `@/domains/artifacts`, never the inner paths — same convention the
 * other domains keep (`runs`, `inbox`, `chat`).
 */

export type {
  ArtifactMime,
  VisualArtifact,
  VisualArtifactFilters,
  VisualArtifactPage,
} from "./model/types"
export {
  ARTIFACT_MIME,
  isImageMime,
  isSandboxedDocumentMime,
} from "./model/types"

export { evidenceForRun, latestPngForTicket } from "./model/filter-artifacts"

export {
  mapVisualArtifactListItemToArtifact,
  mapVisualArtifactPageToPage,
} from "./api/mappers"

export {
  useVisualArtifactsQuery,
  visualArtifactContentUrl,
  visualArtifactsQueryKey,
} from "./api/queries"

export { EvidenceViewer } from "./ui/evidence-viewer"
