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
          autoFocus
          value={name}
          disabled={busy}
          placeholder="who this is"
          onValueChange={setName}
        />
        <TextField
          id="user-email"
          label="address"
          type="email"
          value={email}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder="name@example.com"
          error={attempted ? addressError : null}
          onValueChange={setEmail}
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
          disabled={busy || name.trim().length === 0}
          aria-busy={busy || undefined}
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
