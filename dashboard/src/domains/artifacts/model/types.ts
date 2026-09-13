/**
 * What a visual artifact looks like on the dashboard.
 *
 * The kubb-generated `VisualArtifactListItem` is the wire row; the domain
 * type this file declares is the shape the screens consume. The mapper in
 * `api/mappers.ts` does the wire → domain translation (size coerced to
 * `number`, etc.), so neither the run page nor the inbox nor the chat has
 * to know about the backend's `int64`-as-string quirk.
 *
 * Three surfaces render the same shape:
 *
 * 1. **Run detail** — an evidence strip next to the pr-report of one run.
 *    The list query is filtered by `runId`.
 * 2. **Ticket inbox** — a thumbnail in each row, the latest png linked to
 *    that ticket. The list query is filtered by `ticketId`.
 * 3. **Chat thread** — a card per `artifact-ref` part in the turn, each
 *    carrying the artifact's `id` so the renderer can open the same pane.
 *
 * The dispatch in the UI is **mime**, not a `kind` enum — every new format
 * the worker publishes becomes a new entry in `VisualArtifactLimits`, not a
 * new branch in the dispatcher. The two predicates below
 * (`isImage` / `isSandboxedDocument`) read those limits through the same
 * surface, so when a fifth mime type lands the only place that changes is
 * `shared/api/mock/artifacts.seed.ts` and the bytes the worker uploads.
 */
export interface VisualArtifact {
  /** Stable host id (UUID), used in query strings and the content URL. */
  id: string
  /** Owning project — necessary scope filter on the content GET. */
  projectId: string
  /** Original file name from the publisher. */
  filename: string
  /** MIME — the single dispatch axis across every surface. */
  contentType: string
  /** Size in bytes, normalised from the wire's `number | string`. */
  sizeBytes: number
  /** Optional human title from the publisher (slice 4 — brain mockup). */
  title: string | null
  /** ISO-8601 publish time, verbatim from the wire. */
  createdAt: string
  /** Who published — `brain` or `worker`, the only two values today. */
  createdBy: string
  /** Optional links the artifact is bound to. */
  runId: string | null
  workItemId: string | null
  sessionId: string | null
  ticketId: string | null
  /** Monotonic per id; 1 on the first publish. */
  version: number
}

/**
 * The page envelope the host returns. Empty list when the project has no
 * visual artifacts yet — exactly what the dashboard renders as "no
 * evidence" (see `EvidenceStrip` in runs and the ticket thumbnail absent).
 */
export interface VisualArtifactPage {
  projectId: string
  items: VisualArtifact[]
}

/**
 * Filters the list endpoint accepts. Per-axis arguments are independent —
 * `runId` and `ticketId` are both client-side narrowing, the host's EF
 * scope filter guarantees the project boundary.
 */
export interface VisualArtifactFilters {
  runId?: string
  workItemId?: string
  ticketId?: string
}

/**
 * The mime types the host currently accepts (slice 1 of issue #51).
 *
 * Kept in the dashboard because the dispatcher must agree with the host
 * without a round-trip — anything not in this set was either rejected at
 * publish (413/415) or never had a publish path wired. New entries land
 * here when the host's `VisualArtifactLimits` grows.
 */
export const ARTIFACT_MIME = {
  png: "image/png",
  html: "text/html",
  svg: "image/svg+xml",
} as const

export type ArtifactMime = (typeof ARTIFACT_MIME)[keyof typeof ARTIFACT_MIME]

/**
 * Browser-rendered as `<img>` directly. The host sets short cache +
 * `nosniff` on the response (issue #51 / §Acceptance 8), so a `<img>` on
 * the dashboard origin does not touch the same-origin cookies — the cookie
 * auth belongs to the SPA's host, and the content GET rides them along.
 *
 * SVG is not in this set: an SVG from a worker is markup, not an image,
 * and pasting one into `<img>` lets an attacker read attributes via
 * `currentSrc` without the sandbox/CSP stack the document case needs.
 */
export function isImageMime(contentType: string): boolean {
  return contentType.toLowerCase() === ARTIFACT_MIME.png
}

/**
 * Browser-rendered in `<iframe sandbox="allow-scripts">` (no
 * `allow-same-origin`). HTML and SVG both carry script-capable markup from
 * a publisher the dashboard does not trust — `text/html` directly and
 * `image/svg+xml` with its `<script>` corner — and the iframe-sandbox-
 * plus-CSP combo is the security model the backend pins in
 * `VisualArtifactsResponseHelpers.ApplyContentHeaders` (issue #51 §Read).
 */
export function isSandboxedDocumentMime(contentType: string): boolean {
  const lower = contentType.toLowerCase()
  return lower === ARTIFACT_MIME.html || lower === ARTIFACT_MIME.svg
}
