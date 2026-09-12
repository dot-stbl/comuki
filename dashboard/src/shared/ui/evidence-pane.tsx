import { Dialog, Heading, Modal, ModalOverlay } from "react-aria-components"

import { Button } from "./button"
import { Tooltip } from "./tooltip"

import styles from "./evidence-pane.module.css"

/**
 * A modal viewer for a single visual artifact, opened from any of the
 * three surfaces in issue #51 (run evidence, ticket thumbnail, chat
 * artifact-ref).
 *
 * Two mime paths land through one component:
 *
 * - `image/png` → `<img src={url}>` directly. The cookie auth rides the
 *   GET; the host returns `X-Content-Type-Options: nosniff` + 5min cache.
 * - `text/html` and `image/svg+xml` → `<iframe sandbox="allow-scripts">`
 *   WITHOUT `allow-same-origin`. The strict CSP on the host's response
 *   (`VisualArtifactsResponseHelpers.ApplyContentHeaders`) blocks
 *   same-origin / eval / remote-frame; the iframe sandbox is the
 *   matching browser-side guarantee. Without `allow-same-origin`, the
 *   embedded document cannot reach the dashboard's cookies — that is
 *   the whole point of the combo, and any future change that re-adds the
 *   flag is a security regression.
 *
 * Same pane, no header chrome differentiation — the bar shows the
 * filename so the operator knows which row they opened.
 */
export interface EvidencePaneProps {
  open: boolean
  onClose: () => void
  /** Absolute URL the dashboard fetches with the cookie. */
  src: string
  /** MIME of the body. The pane asks `image/png` vs the iframe pair. */
  contentType: string
  /** Filename — read aloud in the dialog's bar, copyable on demand. */
  filename: string
  /** Optional title for the dialog heading (defaults to the filename). */
  title?: string
  /** Size in bytes, shown in the data voice when known. */
  sizeBytes?: number
}

/**
 * The visual-artifact viewer modal.
 *
 * Three concerns the body has to answer at once:
 *
 * 1. **Mime dispatch.** A PNG opens in a sized `<img>`. A document opens
 *    in a fixed-aspect `<iframe>`. Anything else leaves the pane open
 *    but empty — callers gate `open` on a known mime (the predicate in
 *    `domains/artifacts/model/types`).
 * 2. **No same-origin.** The iframe is `sandbox="allow-scripts"` and
 *    nothing else. See the props-and-security note above.
 * 3. **Escape and the explicit close.** `BottomSheet`'s modal styling
 *    is overkill for a viewer; `react-aria-components`' `ModalOverlay`
 *    gives the same escape-key gesture and focus trap for a regular
 *    modal. The close button in the bar is the keyboard equivalent.
 */
export function EvidencePane({
  open,
  onClose,
  src,
  contentType,
  filename,
  title,
  sizeBytes,
}: EvidencePaneProps) {
  const heading = title ?? filename
  const lower = contentType.toLowerCase()
  const isPng = lower === "image/png"
  const isHtmlOrSvg =
    lower === "text/html" || lower === "image/svg+xml"

  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={(isOpen) => {
        if (!isOpen) {
          onClose()
        }
      }}
      isDismissable
      className={styles.scrim}
    >
      <Modal className={styles.modal}>
        <Dialog className={styles.dialog} data-test="evidence-pane">
          <header className={styles.bar}>
            <Heading slot="title" className={styles.title}>
              {heading}
            </Heading>
            {typeof sizeBytes === "number" ? (
              <span className={styles.meta}>{formatBytes(sizeBytes)}</span>
            ) : null}
            <Tooltip content="Close — escape">
              <Button
                variant="ghost"
                size="icon-sm"
                data-test="evidence-pane-close"
                aria-label="Close evidence"
                onClick={onClose}
              >
                <span aria-hidden="true">×</span>
              </Button>
            </Tooltip>
          </header>

          <div className={styles.body}>
            {isPng ? (
              <img
                src={src}
                alt={filename}
                className={styles.image}
                draggable={false}
              />
            ) : isHtmlOrSvg ? (
              <iframe
                src={src}
                title={heading}
                sandbox="allow-scripts"
                className={styles.iframe}
                referrerPolicy="no-referrer"
              />
            ) : (
              <p className={styles.empty}>
                no viewer for {contentType || "unknown"} — bytes live at the
                URL above
              </p>
            )}
          </div>

          <footer className={styles.foot}>
            <a
              href={src}
              className={styles.download}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`Open ${filename} in a new tab`}
            >
              open in a new tab
            </a>
            <span className={styles.urlLabel}>{filename}</span>
          </footer>
        </Dialog>
      </Modal>
    </ModalOverlay>
  )
}

/**
 * Bytes → a human-readable label. `KiB`/`MiB` because the dashboard
 * speaks IEC for storage (issue #51 caps png at 5 MiB; the rule applies
 * upstream, not here), and the operator wants a number with a unit, not
 * an indicator.
 */
function formatBytes(value: number): string {
  if (value < 1024) {
    return `${value} B`
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KiB`
  }
  return `${(value / (1024 * 1024)).toFixed(2)} MiB`
}
