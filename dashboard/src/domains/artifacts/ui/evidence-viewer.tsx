import { useCallback, useMemo, useState } from "react"

import { EvidencePane, EvidenceThumbnail } from "@/shared/ui"
import { cn } from "@/shared/lib/utils"

import { visualArtifactContentUrl } from "../api/queries"
import type { VisualArtifact } from "../model/types"

import styles from "./evidence-viewer.module.css"

/**
 * The viewer + thumbnail + state keeper, packaged for the three surfaces
 * (run, ticket, chat).
 *
 * Why a single component for all three: every caller does the same thing
 * — render a thumbnail grid, keep the selected artifact in state, mount
 * the modal. Duplicating that logic in three pages means three places to
 * forget to dismiss, three reset-on-projection bugs, and three ways to
 * open a stale artifact after navigation. Composed here, the surface
 * passes the project's artifacts and a layout choice.
 *
 * State is local rather than global because the artifact selected on one
 * page has nothing to say on the next — the inbox page and the run page
 * each have their own viewer, opening them in quick succession is two
 * modals stacked over different routes, never a single modal answering
 * for both. The chat console is the third surface.
 */
export interface EvidenceViewerProps {
  /** The slices the caller has already filtered down to. */
  items: readonly VisualArtifact[]
  /** Stable id of the project the artifacts live under. */
  projectId: string
  /** Where the content proxy base URL lives (mock-aware). */
  baseUrl: string
  /** Optional label for the strip header (e.g. "evidence", "thumbnails"). */
  label?: string
  /** Empty-state placeholder when there is nothing to render. */
  emptyLabel: string
  /** Thumbnail size — `md` for the strip, `sm` for the ticket row. */
  size?: "sm" | "md"
  /** Whether to lay out the thumbnails horizontally or as a wrap. */
  variant?: "strip" | "row"
  className?: string
}

/**
 * The visible strip + the modal it opens.
 *
 * Lives in the artifacts domain rather than the page because every caller
 * passes the same trio: `items` (already filtered by run / ticket / id),
 * `projectId` (for the content URL), `baseUrl` (read once from
 * `env.apiBaseUrl`). The page renders `<EvidenceViewer items={...} />`
 * and stops worrying about how a click becomes a pane.
 */
export function EvidenceViewer({
  items,
  projectId,
  baseUrl,
  label,
  emptyLabel,
  size = "md",
  variant = "strip",
  className,
}: EvidenceViewerProps) {
  // `string | null`, not `string | undefined`: `null` reads as "nothing
  // open right now", a state with a single value rather than the
  // three-state `present | absent | never-was` distinction `undefined`
  // would silently introduce (and the modal's own `open={false}` already
  // treats the absence the same way). The artifact reference is the
  // pane's only state — there is no "draft of which artifact you would
  // open next", so the modal is closed exactly when there is no id.
  const [openId, setOpenId] = useState<string | null>(null)
  const openArtifact = useMemo(
    () =>
      openId ? (items.find((entry) => entry.id === openId) ?? null) : null,
    [openId, items]
  )
  const close = useCallback(() => setOpenId(null), [])
  const select = useCallback((id: string) => setOpenId(id), [])

  if (items.length === 0) {
    return (
      <div
        className={cn(styles.empty, className)}
        data-test="evidence-empty"
        role="note"
      >
        {label ? <span className={styles.emptyLabel}>{label}</span> : null}
        <span className={styles.emptyMessage}>{emptyLabel}</span>
      </div>
    )
  }

  return (
    <div className={cn(styles.viewer, className)} data-test="evidence-viewer">
      {label ? <span className={styles.label}>{label}</span> : null}
      <div
        className={cn(
          styles.row,
          variant === "strip" ? styles.strip : styles.inline
        )}
        data-test="evidence-thumbs"
      >
        {items.map((entry) => (
          <EvidenceThumbnail
            key={entry.id}
            src={visualArtifactContentUrl(baseUrl, projectId, entry.id)}
            contentType={entry.contentType}
            filename={entry.filename}
            title={entry.title ?? entry.filename}
            size={size}
            data-test="evidence-thumb"
            onActivate={() => select(entry.id)}
          />
        ))}
      </div>
      <EvidencePane
        open={openArtifact !== null}
        onClose={close}
        src={
          openArtifact
            ? visualArtifactContentUrl(baseUrl, projectId, openArtifact.id)
            : ""
        }
        contentType={openArtifact?.contentType ?? ""}
        filename={openArtifact?.filename ?? ""}
        title={openArtifact?.title ?? openArtifact?.filename ?? ""}
        sizeBytes={openArtifact?.sizeBytes}
      />
    </div>
  )
}
