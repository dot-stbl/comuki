import { Dialog, Heading, Modal, ModalOverlay } from "react-aria-components"

import { cn } from "@/shared/lib/utils"

import { Button } from "./button"
import styles from "./confirm-dialog.module.css"

export interface ConfirmDialogProps {
  open: boolean
  title: string
  body: string
  confirmLabel: string
  cancelLabel: string
  onConfirm: () => void
  onCancel: () => void
  danger?: boolean
}

export function ConfirmDialog({
  open,
  title,
  body,
  confirmLabel,
  cancelLabel,
  onConfirm,
  onCancel,
  danger = false,
}: ConfirmDialogProps) {
  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={(isOpen) => {
        if (!isOpen) {
          onCancel()
        }
      }}
      isDismissable={false}
      className={cn(styles.scrim)}
    >
      <Modal className={cn(styles.modal)}>
        <Dialog className={cn(styles.dialog)} data-test="confirm-dialog">
          <Heading slot="title" className={cn(styles.title)}>
            {title}
          </Heading>
          <p className={styles.body}>{body}</p>
          <div className={styles.footer}>
            <Button
              variant="secondary"
              data-test="confirm-dialog-cancel"
              onClick={onCancel}
            >
              {cancelLabel}
            </Button>
            <Button
              variant={danger ? "destructive" : "default"}
              data-test="confirm-dialog-confirm"
              onClick={onConfirm}
            >
              {confirmLabel}
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  )
}
