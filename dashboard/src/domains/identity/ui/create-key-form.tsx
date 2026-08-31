import { useEffect, useState } from "react"
import type { FormEvent } from "react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import type { CreateApiKeyInput } from "@/domains/identity/model/types"
import { useCan } from "@/shared/session"
import { Button, Notice, SelectField, TextField } from "@/shared/ui"

const LIFETIMES = [
  { value: "0", label: "no expiry" },
  { value: "30", label: "in 30 days" },
  { value: "90", label: "in 90 days" },
  { value: "365", label: "in a year" },
]

/** The chosen lifetime as an ISO day, or `null` for a key that never expires. */
function expiryDay(days: string): string | null {
  const count = Number(days)
  if (!Number.isFinite(count) || count <= 0) {
    return null
  }
  return new Date(Date.now() + count * 86_400_000).toISOString().slice(0, 10)
}

export interface CreateKeyFormProps {
  busy?: boolean
  onCreate: (input: CreateApiKeyInput) => void
  onCancel: () => void
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Making a key: a name, a lifetime, and the warning that comes before both.
 *
 * The rule this form is built around: **the plaintext is shown exactly once,
 * and the screen says so before it is generated, not after.** A person who did
 * not know cannot get it back — there is no second showing to recover it from,
 * because the store keeps only the prefix — so the warning is above the button
 * that creates the key rather than beside the value that has already appeared.
 * A rule explained after the fact is not an explanation; it is an apology.
 *
 * The showing itself is not here and is not on this page's URL. See
 * `key-secret-dialog.tsx` for why.
 */
export function CreateKeyForm({
  busy = false,
  onCreate,
  onCancel,
  onDirtyChange,
}: CreateKeyFormProps) {
  const manage = useCan("identity.manage")

  const [name, setName] = useState("")
  const [lifetime, setLifetime] = useState("0")

  const dirty = name !== "" || lifetime !== "0"

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (manage.denial || busy || !name.trim()) {
      return
    }
    onCreate({ name: name.trim(), expiresAt: expiryDay(lifetime) })
  }

  return (
    <FormLayout data-test="create-key" onSubmit={submit}>
      <FormFields>
        {/* Before the key exists, not after it has scrolled away. */}
        <Notice>
          The secret appears once, in a dialog over this page. Copy it there —
          it is stored hashed, it is not in this page's address, and it cannot
          be shown again.
        </Notice>

        <TextField
          id="key-name"
          label="name"
          autoFocus
          value={name}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder="what this key is for"
          hint="The name is how the key is recognised in the list. It is not part of the secret."
          onValueChange={setName}
        />

        <SelectField
          id="key-lifetime"
          label="expires"
          value={lifetime}
          disabled={busy}
          options={LIFETIMES}
          hint="A key with no expiry is a key nobody will notice is still working."
          onValueChange={setLifetime}
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
          Create key
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
