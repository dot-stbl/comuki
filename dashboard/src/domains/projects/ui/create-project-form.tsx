import { useEffect, useState } from "react"
import type { FormEvent } from "react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import { slugify, validateSlug } from "@/domains/projects/model/slug"
import type { CreateProjectInput } from "@/domains/projects/model/types"
import { useCan } from "@/shared/session"
import { Button, TextField } from "@/shared/ui"

export interface CreateProjectFormProps {
  /** Slugs already in use — the handle has to be unique to be a handle. */
  takenSlugs: readonly string[]
  busy?: boolean
  onCreate: (input: CreateProjectInput) => void
  onCancel: () => void
  /** Tells the page whether there is anything here worth asking about. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Three fields, one of which is not prose.
 *
 * The slug is the handle that shows up as a column in the runs list, the queue
 * and every role scope, so the form treats it as a value: it is proposed from
 * the name while nobody has touched it, it stops being proposed the instant
 * somebody does, and it is never silently rewritten. A handle the operator did
 * not choose is a handle they will not recognise where it lands.
 *
 * The submit is disabled for *busy* and for a missing name — the two things
 * that make the act impossible — and refuses on an invalid slug by showing the
 * reason instead. Disabling on the slug would hide the message behind a control
 * that cannot be pressed, which is exactly the failure the `denied` rule exists
 * to prevent, one field over.
 *
 * No router, no shell, no mutation: the page above it owns all three. What is
 * left here is the three fields and the rules about them, which is the part
 * worth testing on its own.
 */
export function CreateProjectForm({
  takenSlugs,
  busy = false,
  onCreate,
  onCancel,
  onDirtyChange,
}: CreateProjectFormProps) {
  // A platform act: it reads platform roles alone, so no project id goes in.
  // Being project-admin of three projects must never open this.
  const create = useCan("projects.create")

  const [name, setName] = useState("")
  const [slug, setSlug] = useState("")
  const [repo, setRepo] = useState("")
  const [slugTouched, setSlugTouched] = useState(false)
  const [attempted, setAttempted] = useState(false)

  const slugError = validateSlug(slug, takenSlugs)
  const showSlugError = (slugTouched || attempted) && slugError

  const dirty = name !== "" || slug !== "" || repo !== ""

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setAttempted(true)
    const trimmedName = name.trim()
    if (create.denial || busy || !trimmedName || slugError) {
      return
    }
    onCreate({
      name: trimmedName,
      slug: slug.trim(),
      gitProfileRepo: repo.trim() || null,
    })
  }

  return (
    <FormLayout data-test="create-project" onSubmit={submit}>
      <FormFields>
        <TextField
          id="project-name"
          label="name"
          autoFocus
          value={name}
          disabled={busy}
          placeholder="what this project is, in a few words"
          onValueChange={(next) => {
            setName(next)
            if (!slugTouched) {
              setSlug(slugify(next))
            }
          }}
        />

        <TextField
          id="project-slug"
          label="slug"
          value={slug}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder="lowercase, hyphens, no spaces"
          hint="Shown as a column in every list in the product. Lowercase letters, digits and hyphens."
          error={showSlugError ? slugError : null}
          onValueChange={(next) => {
            setSlugTouched(true)
            setSlug(next)
          }}
        />

        <TextField
          id="project-repo"
          label="git profile repository"
          value={repo}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder="git@github.com:org/worker-profiles.git"
          hint="Optional. Where this project's worker profiles are authored — leave it empty to run on the platform defaults."
          onValueChange={setRepo}
        />
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="form-submit"
          denied={create.denial}
          disabled={busy || name.trim().length === 0}
          aria-busy={busy || undefined}
        >
          Create project
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
