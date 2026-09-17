import { useEffect, useState } from "react"
import type { FormEvent } from "react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import type { InviteUserInput } from "@/domains/identity/model/types"
import { useCan } from "@/shared/session"
import { Button, SelectField, TextField } from "@/shared/ui"

/** Deliberately loose: an address is validated by sending to it, not by a regex. */
const ADDRESS = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

const ARRIVAL = [
  { value: "invite", label: "send an invitation" },
  { value: "local", label: "create a local account" },
]

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
  // A platform act, asked without a project: platform roles alone answer for
  // Identity, and no project role has ever opened it.
  const manage = useCan("identity.manage")

  const [name, setName] = useState("")
  const [email, setEmail] = useState("")
  const [arrival, setArrival] = useState("invite")
  const [emailTouched, setEmailTouched] = useState(false)
  const [attempted, setAttempted] = useState(false)

  const address = email.trim().toLowerCase()
  const addressError =
    address.length === 0
      ? "an address is required"
      : !ADDRESS.test(address)
        ? "that does not look like an address"
        : takenAddresses.includes(address)
          ? "somebody already has that address"
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
          label="name"
          /* Both fields carry the marker, because both genuinely gate the
             act — the button refuses without a name, and the handler refuses
             without a valid address. Marking only the one the button watches
             would promise a rule this form does not have. */
          required
          autoFocus
          value={name}
          disabled={busy}
          placeholder="who this is"
          onValueChange={setName}
        />
        <TextField
          id="user-email"
          label="address"
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
          label="how"
          value={arrival}
          disabled={busy}
          options={ARRIVAL}
          hint="An invitation waits to be accepted. A local account works immediately."
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
          {arrival === "invite" ? "Send invitation" : "Create account"}
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
