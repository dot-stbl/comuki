import { useCallback, useEffect, useRef, type ReactNode } from "react"
import { Maximize2, Minimize2, X } from "lucide-react"
import { Dialog, Heading, Modal, ModalOverlay } from "react-aria-components"
import type { PanelImperativeHandle } from "react-resizable-panels"

import { Button } from "../button"
import { SplitPane, SplitPanel, SplitSeparator } from "../split-pane"
import { Tooltip } from "../tooltip"

import styles from "./bottom-sheet.module.css"

/**
 * A modal panel docked to the bottom of the window, resizable by its top edge.
 *
 * ## What it is, and what it costs
 *
 * It is a *modal*: a scrim, a focus trap, escape closes, and focus returns to
 * whatever opened it. That is the whole contract of `ModalOverlay` / `Modal` /
 * `Dialog`, which is why they are what this is built on rather than a fixed
 * `<div>` with a hand-rolled key handler — `ConfirmDialog` and `FormDialog`
 * already stand on them and get the same six behaviours for free.
 *
 * The cost is stated rather than hidden: a scrim means the board underneath is
 * not readable while the sheet is open. A caller that needs the reader to keep
 * the screen behind them wants a docked panel, not this.
 *
 * ## Resize, and why it is a pane group
 *
 * The top edge is a `SplitSeparator` and the depth is a `SplitPanel`, so the
 * drag, the keyboard resize, the `role="separator"` semantics and the
 * remembered position are the product's one resize implementation rather than a
 * second one written against `pointerdown`. The invisible panel above the
 * separator is what the sheet is measured against; it is transparent, and the
 * scrim shows through it.
 *
 * ## Full window, and why it is not the same gesture
 *
 * `expanded` is a control, not a drag: the caller is told about it because the
 * two states usually show *different amounts* of the same thing, and only the
 * caller knows what to put in each. While the sheet is full the separator is
 * disabled and no layout is written down — so the depth the operator dragged is
 * still there when they come back out of it, rather than having been
 * overwritten with "the whole window". That is the same `shouldPersist` gate
 * the shell's rail keeps, asked about a different environment.
 */
export interface BottomSheetProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The sheet's own name — its bar's heading, and the dialog's label. */
  title: string
  /** Controls that belong to this sheet, ahead of the two the sheet owns. */
  toolbar?: ReactNode
  children: ReactNode
  /**
   * localStorage key for the dragged depth. Omit and the sheet opens at
   * `defaultSize` every time.
   */
  storageKey?: string
  /** The share of the window the *panel above* takes when nothing is stored. */
  defaultSize?: string
  /** How far the sheet may be pulled up, as the panel above's floor. */
  minSize?: string
  /** Filling the window, controlled — see the note above. */
  expanded: boolean
  onExpandedChange: (next: boolean) => void
  /** Test hook for the dialog itself; the two controls carry their own. */
  "data-test"?: string
}

export function BottomSheet({
  open,
  onOpenChange,
  title,
  toolbar,
  children,
  storageKey,
  defaultSize = "55%",
  minSize = "12%",
  expanded,
  onExpandedChange,
  "data-test": dataTest = "bottom-sheet",
}: BottomSheetProps) {
  const above = useRef<PanelImperativeHandle | null>(null)
  // Read at write time by `persist`, which runs on a pointer release — long
  // after this effect has told it what state the sheet is in.
  const full = useRef(expanded)

  useEffect(() => {
    full.current = expanded
    const panel = above.current
    if (!panel) {
      return
    }
    if (expanded) {
      if (!panel.isCollapsed()) {
        panel.collapse()
      }
      return
    }
    if (panel.isCollapsed()) {
      // Back to the depth the operator last dragged: the panel remembers its
      // own most recent size, and nothing overwrote the stored layout while
      // the sheet was filling the window.
      panel.expand()
    }
  }, [expanded, open])

  const persist = useCallback(() => !full.current, [])

  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={onOpenChange}
      /* A console is a place somebody is typing. A stray click on the scrim
         must not take it away — the two ways out are both deliberate, and both
         are named: escape, and the control in the bar. The product's other two
         modals answer this question the same way. Both props, belt and braces:
         `isDismissable` is the contract, and the explicit predicate keeps a
         future library default from re-arming the gesture underneath it. */
      isDismissable={false}
      shouldCloseOnInteractOutside={() => false}
      className={styles.scrim}
    >
      <Modal className={styles.modal}>
        <SplitPane
          orientation="vertical"
          storageKey={storageKey}
          shouldPersist={persist}
          className={styles.group}
        >
          {/* The window the sheet is *not* filling. Empty on purpose: it is a
              measurement, and the scrim behind it is what the reader sees. */}
          <SplitPanel
            id="above"
            panelRef={above}
            defaultSize={defaultSize}
            minSize={minSize}
            collapsible
            collapsedSize={0}
            className={styles.above}
          >
            {/* `null`, not omitted: the panel is required to state that it
                holds nothing, the same way the comment above does. */}
            {null}
          </SplitPanel>

          <SplitSeparator
            orientation="vertical"
            aria-label={`Resize ${title}`}
            disabled={expanded}
            className={expanded ? styles.edgeGone : styles.edge}
          />

          <SplitPanel id="sheet" minSize="18%" className={styles.panel}>
            <Dialog
              className={styles.dialog}
              data-test={dataTest}
              data-expanded={expanded || undefined}
              /* No `aria-modal` here: React Aria filters that attribute out
                 on purpose — a Safari bug forces the first focusable element
                 to take focus when a dialog inside an iframe carries it — and
                 spells modality by hiding everything outside the dialog
                 instead. Same reading for a screen reader; the sheet's test
                 asserts that hiding rather than an attribute the library
                 refuses to render. */
            >
              <div className={styles.bar}>
                <Heading slot="title" className={styles.title}>
                  {title}
                </Heading>
                <span className={styles.spacer} />
                {toolbar}
                <Tooltip
                  content={expanded ? "Back to a panel" : "Fill the window"}
                >
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    data-test="bottom-sheet-expand"
                    aria-label={
                      expanded
                        ? `Shrink ${title} back to a panel`
                        : `Fill the window with ${title}`
                    }
                    aria-pressed={expanded}
                    onClick={() => onExpandedChange(!expanded)}
                  >
                    {expanded ? (
                      <Minimize2 aria-hidden="true" />
                    ) : (
                      <Maximize2 aria-hidden="true" />
                    )}
                  </Button>
                </Tooltip>
                <Tooltip content="Close — escape">
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    data-test="bottom-sheet-close"
                    aria-label={`Close ${title}`}
                    onClick={() => onOpenChange(false)}
                  >
                    <X aria-hidden="true" />
                  </Button>
                </Tooltip>
              </div>

              <div className={styles.body}>{children}</div>
            </Dialog>
          </SplitPanel>
        </SplitPane>
      </Modal>
    </ModalOverlay>
  )
}
