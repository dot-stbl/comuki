import type { ReactNode } from "react"
import {
  Dialog as AriaDialog,
  Heading,
  Modal,
  ModalOverlay,
} from "react-aria-components"

import { Button } from "./button"
import { cn } from "@/shared/lib/utils"

import styles from "./dialog.module.css"

/**
 * A modal panel with a title, a body, and an optional footer of action
 * buttons. The shape that `ConfirmDialog` (yes / no) and the runs-page
 * anomaly breakdown (informational) both render from, but stripped of
 * the confirm-specific contract — the caller chooses what sits in the
 * body and which buttons close the dialog.
 *
 * Like the other kit modals, `Dialog` stands on React Aria's
 * `ModalOverlay` / `Modal` / `Dialog` triplet rather than a hand-rolled
 * `<div>` — scrim, focus trap, escape to close, focus return — all of
 * which come from the platform and stop being decisions on this side.
 *
 * `dismissable` defaults to `true` because `Dialog` is informational at
 * heart: a stray click on the dark is a way out, the way a form's
 * half-filled answer is not. `ConfirmDialog` re-arms `dismissable=false`
 * for its own case, but a generic dialog does not have to take that
 * loss.
 */
export interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: ReactNode
  /**
   * The dialog's body. A node rather than a string because the body is
   * often a paragraph with an identifier in it, a figure beside a sentence,
   * or two stacked breakdowns — all of which a flat string would force into
   * one shape.
   */
  children: ReactNode
  /**
   * The dialog's own footer — action buttons the caller authors. The
   * dialog always renders one dismiss action on the left ("Close") so a
   * caller that only authors content still has a way out, and a custom
   * footer sits to the right.
   */
  footer?: ReactNode
  /** The dismiss button's label. "Close" by default. */
  dismissLabel?: string
  /** Close on a click on the scrim. Defaults to true. */
  dismissable?: boolean
  /** Optional size override; default `--modal-w` (26rem). */
  width?: string
  "data-test"?: string
}

export function Dialog({
  open,
  onOpenChange,
  title,
  children,
  footer,
  dismissLabel = "Close",
  dismissable = true,
  width,
  "data-test": dataTest = "dialog",
}: DialogProps) {
  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={onOpenChange}
      isDismissable={dismissable}
      className={cn(styles.scrim)}
    >
      <Modal
        className={cn(styles.modal)}
        style={width ? { maxInlineSize: width } : undefined}
      >
        <AriaDialog className={cn(styles.dialog)} data-test={dataTest}>
          <Heading slot="title" className={cn(styles.title)}>
            {title}
          </Heading>
          <div className={styles.body}>{children}</div>
          <div className={styles.footer}>
            {footer ? (
              <span className={styles.footerActions}>{footer}</span>
            ) : null}
            <Button
              variant="secondary"
              data-test="dialog-close"
              onClick={() => onOpenChange(false)}
            >
              {dismissLabel}
            </Button>
          </div>
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  )
}
