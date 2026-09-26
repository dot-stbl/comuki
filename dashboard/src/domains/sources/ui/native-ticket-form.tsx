import { useEffect, useState } from "react"
import type { FormEvent } from "react"
import { useTranslation } from "react-i18next"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import { parseTicketLabels } from "@/domains/sources/model/providers"
import type { SeedTicketDraft } from "@/shared/api/mock/sources.store"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, SwitchField, TextField, TextareaField } from "@/shared/ui"

export interface NativeTicketFormProps {
  /** The project the ticket lands in — the connection's, never the shift's. */
  projectId: string
  busy?: boolean
  onCreate: (draft: SeedTicketDraft) => void
  onCancel: () => void
  /** Tells the page whether there is anything here worth asking about. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * File a ticket in the product's own intake.
 *
 * The counterpart to every other form in this section: there is no provider, no
 * credential and no filter, because the ticket is being written *here*. Title,
 * body, labels — and one decision that is not a field about the ticket at all.
 *
 * "Straight to work" starts a run the moment this saves, which is the same
 * thing an `inbox.take` on the catalog does a minute later. It is a switch
 * rather than a second button because the two acts differ by exactly one bit,
 * and a pair of buttons would make the operator infer which one also created
 * the ticket.
 *
 * The labels box splits on a comma and nothing more, and the hint says so out
 * loud — because the field directly above it on the source's own page is the
 * filter expression, which is *emphatically* not a list and is never parsed.
 * Two boxes of comma-ish text, one with a meaning and one deliberately without,
 * is exactly the pair that needs saying rather than assuming.
 *
 * Gated on `inbox.take` rather than on `sources.edit`: putting work into intake
 * is a member's act, and requiring a project administrator to write down a bug
 * would be the wrong shape even though this section is an administrator's.
 */
export function NativeTicketForm({
  projectId,
  busy = false,
  onCreate,
  onCancel,
  onDirtyChange,
}: NativeTicketFormProps) {
  const { t } = useTranslation("sources")
  const session = useSession()

  const [title, setTitle] = useState("")
  const [body, setBody] = useState("")
  const [labels, setLabels] = useState("")
  const [straightToWork, setStraightToWork] = useState(false)

  const trimmedTitle = title.trim()

  const denied = can(session, "inbox.take", projectId)
    ? null
    : needsLabel("inbox.take", projectOf(session, projectId)?.key)

  // Coarse the way `useUnsavedGuard` asks for it: anything typed at all counts,
  // and so does the switch — flipping it is the whole difference between a
  // ticket that waits and a run that has already started.
  const dirty =
    title !== "" || body !== "" || labels !== "" || straightToWork !== false

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (denied || busy || trimmedTitle.length === 0) {
      return
    }
    onCreate({
      projectId,
      title: trimmedTitle,
      body: body.trim(),
      labels: parseTicketLabels(labels),
      straightToWork,
    })
  }

  return (
    <FormLayout data-test="native-ticket" onSubmit={submit}>
      <FormFields>
        <TextField
          id="ticket-title"
          label={t("ticketForm.titleLabel")}
          required
          autoFocus
          value={title}
          disabled={busy}
          placeholder={t("ticketForm.titlePlaceholder")}
          data-test="ticket-title"
          onValueChange={setTitle}
        />

        <TextareaField
          id="ticket-body"
          label={t("ticketForm.bodyLabel")}
          value={body}
          disabled={busy}
          rows={6}
          placeholder={t("ticketForm.bodyPlaceholder")}
          data-test="ticket-body"
          onValueChange={setBody}
        />

        <TextField
          id="ticket-labels"
          label={t("ticketForm.labelsLabel")}
          value={labels}
          disabled={busy}
          placeholder="checkout-web, bug"
          spellCheck={false}
          hint={t("ticketForm.labelsHint")}
          data-test="ticket-labels"
          onValueChange={setLabels}
        />

        <SwitchField
          id="ticket-straight-to-work"
          label={t("ticketForm.straightLabel")}
          checked={straightToWork}
          disabled={busy}
          denied={denied}
          hint={t("ticketForm.straightHint")}
          data-test="ticket-straight-to-work"
          onCheckedChange={setStraightToWork}
        />
      </FormFields>

      <FormActions>
        {/* One bit of difference, said in the button rather than left to be
            inferred from a switch three fields up. `disabled` for a missing
            title — incomplete, not forbidden — and `denied` for the role, and
            the two must not look alike. */}
        <Button
          type="submit"
          data-test="form-submit"
          denied={denied}
          loading={busy}
          disabled={trimmedTitle.length === 0}
        >
          {straightToWork
            ? t("ticketForm.submitStart")
            : t("ticketForm.submit")}
        </Button>
        <Button
          variant="secondary"
          data-test="form-cancel"
          disabled={busy}
          onClick={onCancel}
        >
          {t("actions.cancel")}
        </Button>
      </FormActions>
    </FormLayout>
  )
}
