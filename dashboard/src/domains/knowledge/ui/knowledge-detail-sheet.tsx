import { X } from "lucide-react"
import { Dialog, Heading, Modal, ModalOverlay } from "react-aria-components"

import type { KnowledgeEntry } from "@/domains/knowledge/model/types"
import { Button, Tooltip } from "@/shared/ui"

import { KindMark, PinnedMark, RuleKindMark } from "./knowledge-badges"
import styles from "./knowledge-detail-sheet.module.css"

export interface KnowledgeDetailSheetProps {
  entry: KnowledgeEntry | null
  open: boolean
  onOpenChange: (open: boolean) => void
}

/**
 * One entry, in full, without leaving the list.
 *
 * **The kit has no sheet.** It has a `ConfirmDialog` that asks one question and
 * a `FormDialog` that holds a form, and both are centred boxes — which is the
 * wrong shape for a body of prose read beside the list it came from. So this is
 * built in the domain and reported as a gap rather than added to `shared/ui`.
 *
 * It is the kit's own construction, though, and deliberately `FormDialog`'s
 * sibling rather than a second opinion about what a modal is: React Aria for
 * the behaviour (focus trap, escape, restore, scrim dismissal) and a CSS Module
 * for the look. All that differs is where the box lands — docked to the
 * inline-end edge at full height, so the entry list stays visible behind it and
 * the operator keeps their place in it.
 *
 * A rule's `body` is the one field on this screen written by a human for a
 * human, so it is the only thing here in the interface voice; everything around
 * it is a value.
 */
export function KnowledgeDetailSheet({
  entry,
  open,
  onOpenChange,
}: KnowledgeDetailSheetProps) {
  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={onOpenChange}
      className={styles.scrim}
    >
      <Modal className={styles.sheet}>
        <Dialog className={styles.dialog} data-test="knowledge-sheet">
          {entry ? (
            <>
              <header className={styles.head}>
                <div className={styles.headText}>
                  <Heading slot="title" className={styles.title}>
                    {entry.title}
                  </Heading>
                  <p className={styles.summary}>{entry.summary}</p>
                </div>
                {/* Escape and the scrim both close this; the glyph is the third
                    way, for a pointer that never learned either. */}
                <Tooltip content="Close">
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    data-test="knowledge-sheet-close"
                    aria-label="Close the entry"
                    onClick={() => onOpenChange(false)}
                  >
                    <X aria-hidden="true" />
                  </Button>
                </Tooltip>
              </header>

              <div className={styles.marks}>
                <KindMark kind={entry.kind} />
                {entry.ruleKind ? (
                  <RuleKindMark ruleKind={entry.ruleKind} />
                ) : null}
                {entry.pinned ? (
                  <PinnedMark revision={entry.revision} />
                ) : (
                  <span className={styles.revision}>
                    revision @{entry.revision}
                  </span>
                )}
              </div>

              <dl className={styles.facts}>
                <div className={styles.fact}>
                  <dt className={styles.factName}>scope</dt>
                  <dd className={styles.factValue}>{entry.scope}</dd>
                </div>
                <div className={styles.fact}>
                  <dt className={styles.factName}>updated</dt>
                  <dd className={styles.factValue}>{entry.updated}</dd>
                </div>
              </dl>

              <p className={styles.body}>{entry.body}</p>
            </>
          ) : null}
        </Dialog>
      </Modal>
    </ModalOverlay>
  )
}
