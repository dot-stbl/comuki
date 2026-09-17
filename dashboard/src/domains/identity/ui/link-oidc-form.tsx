import { useEffect, useState } from "react"
import type { FormEvent } from "react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import type { LinkOidcInput, UserRow } from "@/domains/identity/model/types"
import { useCan } from "@/shared/session"
import { Button, TextField } from "@/shared/ui"

export interface LinkOidcFormProps {
  /** The account being linked. The page answers for finding it. */
  user: UserRow
  busy?: boolean
  onLink: (input: LinkOidcInput) => void
  onCancel: () => void
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Linking an identity provider's subject to an account that already exists.
 *
 * OIDC says who you are; it does not say what you hold. Roles never come from
 * the provider — they are granted here and only here — so this form writes one
 * field and deliberately offers nothing else.
 */
export function LinkOidcForm({
  user,
  busy = false,
  onLink,
  onCancel,
  onDirtyChange,
}: LinkOidcFormProps) {
  const manage = useCan("identity.manage")
  const [subject, setSubject] = useState("")
  const [touched, setTouched] = useState(false)
  const [attempted, setAttempted] = useState(false)

  const trimmed = subject.trim()
  const error = trimmed.length === 0 ? "a subject is required" : null
  /* Edited-or-already-tried — the same model as every other form here; see
     `projects/ui/create-project-form`, which is the reference spelling. */
  const showError = (touched || attempted) && error

  useEffect(() => {
    onDirtyChange?.(subject !== "")
  }, [subject, onDirtyChange])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setAttempted(true)
    if (manage.denial || busy || error) {
      return
    }
    onLink({ userId: user.id, subject: trimmed })
  }

  return (
    <FormLayout data-test="link-oidc" onSubmit={submit}>
      <FormFields>
        <TextField
          id="oidc-subject"
          label="subject"
          /* The one field on this form, and the handler refuses without it. */
          required
          autoFocus
          value={subject}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder="oidc|provider|00000000"
          hint="The `sub` claim the provider issues for this person."
          error={showError ? error : null}
          onValueChange={(next) => {
            setTouched(true)
            setSubject(next)
          }}
        />
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="form-submit"
          denied={manage.denial}
          loading={busy}
        >
          Link subject
        </Button>
        <Button
          variant="secondary"
          data-test="form-cancel"
          disabled={busy}
          onClick={onCancel}
        >
          Cancel
        </Button>
      </FormActions>
    </FormLayout>
  )
}
