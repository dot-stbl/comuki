import { FileText } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import styles from "./evidence-thumbnail.module.css"

/**
 * An inline thumbnail for one visual artifact, used by:
 *
 * - The run detail's evidence strip (next to the pr-report).
 * - The ticket inbox's row thumbnail (the latest png linked to the
 *   ticket, see `latestPngForTicket`).
 * - The chat thread's `artifact-ref` card.
 *
 * The click target opens the same `EvidencePane`. The thumbnail is
 * **never** the iframe path — a thumbnail is by definition preview-sized,
 * and a document can be tall; an `<img src>` for the png branch and a
 * labelled icon for the document branch are the two honest thumbnails.
 *
 * Sandbox + CSP still apply: this is a `<img>` for the image/png branch
 * (cookie-authed origin, native loading) and an inert icon for the
 * document branch — never the document itself.
 */
export interface EvidenceThumbnailProps {
  /** Absolute URL the browser fetches to draw the image. */
  src: string
  /** MIME — drives the `<img>` vs icon branch. */
  contentType: string
  filename: string
  /** Optional title for the screen reader (falls back to the filename). */
  title?: string
  /** Optional size class. `md` is the strip's natural size. */
  size?: "sm" | "md"
  onActivate: () => void
  /** Test hook. */
  "data-test"?: string
}

export function EvidenceThumbnail({
  src,
  contentType,
  filename,
  title,
  size = "md",
  onActivate,
  "data-test": dataTest,
}: EvidenceThumbnailProps) {
  const lower = contentType.toLowerCase()
  const isPng = lower === "image/png"
  const accessibleTitle = title ?? filename

  return (
    <button
      type="button"
      onClick={onActivate}
      data-test={dataTest ?? "evidence-thumbnail"}
      aria-label={`Open ${accessibleTitle}`}
      title={accessibleTitle}
      className={cn(styles.thumb, size === "sm" && styles.sm)}
    >
      {isPng ? (
        <img
          src={src}
          alt={accessibleTitle}
          className={styles.image}
          draggable={false}
        />
      ) : (
        <span className={styles.docBadge} aria-hidden="true">
          <FileText className={styles.docIcon} />
          <span className={styles.docKind}>{labelForMime(contentType)}</span>
        </span>
      )}
    </button>
  )
}

/**
 * A short label for the document kinds the host serves.
 *
 * `text/html` reads as the same single word only because the inbox row
 * already had a label nearby; the chat row benefits from a `document`
 * vs `svg` distinction. The mapper returns the *mime's word* the
 * dashboard's voice recognises, not the wire's full qualifier.
 */
function labelForMime(contentType: string): string {
  const lower = contentType.toLowerCase()
  if (lower === "text/html") {
    return "html"
  }
  if (lower === "image/svg+xml") {
    return "svg"
  }
  return "file"
}
