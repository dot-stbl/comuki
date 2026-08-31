import type { FormEvent, ReactNode } from "react"
import { Dialog, Heading, Modal, ModalOverlay } from "react-aria-components"

import { cn } from "@/shared/lib/utils"

import { Button } from "../button"
import styles from "./form.module.css"

export interface FormDialogProps {
  open: boolean
  title: string
  /** One line under the title saying what the form does. */
  description?: ReactNode
  children: ReactNode
  submitLabel: string
  cancelLabel?: string
  /** The act is running. `disabled`, because busy is not a denial. */
  busy?: boolean
  /** The form is incomplete. `disabled`, because invalid is not a denial. */
  submitDisabled?: boolean
  /**
   * This role may not perform the act — the sentence naming what would.
   *
   * `denied` and never `disabled`: a disabled control fires no pointer events,
   * so the sentence explaining it is unreachable by pointer and out of the tab
   * order both. The form stays fillable and the submit explains itself.
   */
  denied?: string | null
  /** A control that belongs at the far left of the footer — a test button. */
  footerLead?: ReactNode
  /** A form with three sections needs more room than a confirm does. */
  wide?: boolean
  onSubmit: () => void
  onCancel: () => void
  /**
   * Replaces the whole footer — for a dialog that is finished, not pending.
   *
   * The created key is the case: once the secret is on screen there is nothing
   * left to submit and nothing left to cancel, so the pair of buttons that says
   * "decide" would be lying about what is left to decide. `footerLead` still
   * applies, because a lead control is a step in the form rather than a way out
   * of it and a finished form has no steps left either.
   */
  footer?: ReactNode
}

/**
 * The dialog a form lives in.
 *
 * React Aria for the modal behaviour (focus trap, escape, restore) and a CSS
 * Module for the look, which is the kit's own classification for a complex
 * interactive primitive — `ConfirmDialog` is built the same way and this is
 * deliberately its sibling rather than a second opinion about what a modal is.
 *
 * The two now differ only in what they are for. A confirm asks one question
 * about an act already chosen; this holds the choosing. Both entrances sit
 * behind `prefers-reduced-motion`.
 */
export function FormDialog({
  open,
  title,
  description,
  children,
  submitLabel,
  cancelLabel = "Cancel",
  busy = false,
  submitDisabled = false,
  denied,
  footerLead,
  wide = false,
  onSubmit,
  onCancel,
  footer,
}: FormDialogProps) {
  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (denied || busy || submitDisabled) {
      return
    }
    onSubmit()
  }

  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={(next) => {
        if (!next) {
          onCancel()
        }
      }}
      className={styles.scrim}
    >
      <Modal className={cn(styles.modal, wide && styles.wide)}>
        <Dialog className={styles.dialog} data-test="form-dialog">
          <form className={styles.form} onSubmit={submit}>
            <Heading slot="title" className={styles.title}>
              {title}
            </Heading>
            {description ? (
              <p className={styles.description}>{description}</p>
            ) : null}
            <div className={styles.body}>{children}</div>
            <div className={styles.footer}>
              {footerLead ? (
                <span className={styles.footerLead}>{footerLead}</span>
              ) : null}
              {footer ?? (
                <>
                  <Button
                    variant="secondary"
                    data-test="form-cancel"
                    disabled={busy}
                    onClick={onCancel}
                  >
                    {cancelLabel}
                  </Button>
                  <Button
                    type="submit"
                    data-test="form-submit"
                    denied={denied}
                    disabled={busy || submitDisabled}
                    aria-busy={busy || undefined}
                  >
                    {submitLabel}
                  </Button>
                </>
              )}
            </div>
          </form>
        </Dialog>
      </Modal>
    </ModalOverlay>
  )
}
