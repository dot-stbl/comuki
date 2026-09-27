import { useEffect, useState } from "react"
import type { FormEvent } from "react"
import { useTranslation } from "react-i18next"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import type { InviteUserInput } from "@/domains/identity/model/types"
import { useCan } from "@/shared/session"
import { Button, SelectField, TextField } from "@/shared/ui"

/** Deliberately loose: an address is validated by sending to it, not by a regex. */
const ADDRESS = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export interface InviteUserFormProps {
  /** Addresses already on the platform — an account is its address. */
  takenAddresses: readonly string[]
  busy?: boolean
  onInvite: (input: InviteUserInput) => void
  onCancel: () => void
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Two ways for a person to start existing here, and they are genuinely two.
 *
 * An invitation leaves the account waiting for somebody to accept it; a local
 * account is usable the moment it is written. §13 names both, and collapsing
 * them into one button would leave the administrator guessing which of the two
 * they just did — which is exactly what the `account` column then shows.
 */
export function InviteUserForm({
  takenAddresses,
  busy = false,
  onInvite,
  onCancel,
  onDirtyChange,
}: InviteUserFormProps) {
  const { t } = useTranslation("identity")
  // A platform act, asked without a project: platform roles alone answer for
  // Identity, and no project role has ever opened it.
  const manage = useCan("identity.manage")

  const [name, setName] = useState("")
  const [email, setEmail] = useState("")
  const [arrival, setArrival] = useState("invite")
  const [emailTouched, setEmailTouched] = useState(false)
  const [attempted, setAttempted] = useState(false)

  const arrivalOptions = [
    { value: "invite", label: t("inviteForm.arrivalInvite") },
    { value: "local", label: t("inviteForm.arrivalLocal") },
  ]

  const address = email.trim().toLowerCase()
  const addressError =
    address.length === 0
      ? t("inviteForm.addressRequired")
      : !ADDRESS.test(address)
        ? t("inviteForm.addressInvalid")
        : takenAddresses.includes(address)
          ? t("inviteForm.addressTaken")
          : null

  /* Edited-or-already-tried, the one model this product shows a field error
     on — `projects/ui/create-project-form` is the reference. Waiting for the
     submit alone leaves somebody typing into a box that already knows the
     address is taken; showing it before either has happened scolds an empty
     field nobody has reached yet. */
  const showAddressError = (emailTouched || attempted) && addressError

  const dirty = name !== "" || email !== "" || arrival !== "invite"

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setAttempted(true)
    if (manage.denial || busy || !name.trim() || addressError) {
      return
    }
    onInvite({
      name: name.trim(),
      email: address,
      invite: arrival === "invite",
    })
  }

  return (
    <FormLayout data-test="invite-user" onSubmit={submit}>
      <FormFields>
        <TextField
          id="user-name"
          label={t("inviteForm.nameLabel")}
          /* Both fields carry the marker, because both genuinely gate the
             act — the button refuses without a name, and the handler refuses
             without a valid address. Marking only the one the button watches
             would promise a rule this form does not have. */
          required
          autoFocus
          value={name}
          disabled={busy}
          placeholder={t("inviteForm.namePlaceholder")}
          onValueChange={setName}
        />
        <TextField
          id="user-email"
          label={t("inviteForm.addressLabel")}
          required
          type="email"
          value={email}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder="name@example.com"
          error={showAddressError ? addressError : null}
          onValueChange={(next) => {
            setEmailTouched(true)
            setEmail(next)
          }}
        />
        <SelectField
          id="user-arrival"
          label={t("inviteForm.howLabel")}
          value={arrival}
          disabled={busy}
          options={arrivalOptions}
          hint={t("inviteForm.arrivalHint")}
          onValueChange={setArrival}
        />
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="form-submit"
          denied={manage.denial}
          loading={busy}
          disabled={name.trim().length === 0}
        >
          {arrival === "invite"
            ? t("inviteForm.submitInvite")
            : t("inviteForm.submitLocal")}
        </Button>
        <Button
          variant="secondary"
          data-test="form-cancel"
          disabled={busy}
          onClick={onCancel}
        >
          {t("actions.cancel", { ns: "common" })}
        </Button>
      </FormActions>
    </FormLayout>
  )
}
