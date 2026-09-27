import { useEffect, useState } from "react"
import type { FormEvent } from "react"
import { useTranslation } from "react-i18next"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import { slugify, validateSlug } from "@/domains/projects/model/slug"
import type { CreateProjectInput } from "@/domains/projects/model/types"
import { ColorField } from "@/domains/projects/ui/color-field"
import { TagEntryField } from "@/domains/projects/ui/tag-entry-field"
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
 * Six fields, one of which is not prose.
 *
 * The slug is the handle that shows up as a column in the runs list, the queue
 * and every role scope, so the form treats it as a value: it is proposed from
 * the name while nobody has touched it, it stops being proposed the instant
 * somebody does, and it is never silently rewritten. A handle the operator did
 * not choose is a handle they will not recognise where it lands.
 *
 * The three identity fields are optional and never refuse the submit: an icon
 * is an emoji or an image URL exactly as typed, a colour paints the dot and
 * the tag tint (never the interface), and tags are entered as chips — Enter
 * or a comma commits one, Backspace on an empty field removes the last, and a
 * duplicate is refused because the second copy says nothing the first did not.
 *
 * The submit is disabled for *busy* and for a missing name — the two things
 * that make the act impossible — and refuses on an invalid slug by showing the
 * reason instead. Disabling on the slug would hide the message behind a control
 * that cannot be pressed, which is exactly the failure the `denied` rule exists
 * to prevent, one field over.
 *
 * No router, no shell, no mutation: the page above it owns all three. What is
 * left here is the fields and the rules about them, which is the part
 * worth testing on its own.
 */
export function CreateProjectForm({
  takenSlugs,
  busy = false,
  onCreate,
  onCancel,
  onDirtyChange,
}: CreateProjectFormProps) {
  const { t } = useTranslation("projects")

  // A platform act: it reads platform roles alone, so no project id goes in.
  // Being project-admin of three projects must never open this.
  const create = useCan("projects.create")

  const [name, setName] = useState("")
  const [slug, setSlug] = useState("")
  const [repo, setRepo] = useState("")
  const [icon, setIcon] = useState("")
  const [color, setColor] = useState<string | null>(null)
  const [tags, setTags] = useState<string[]>([])
  const [slugTouched, setSlugTouched] = useState(false)
  const [attempted, setAttempted] = useState(false)

  const slugError = validateSlug(slug, takenSlugs)
  /*
   * WHEN A FIELD ERROR IS SHOWN — `touched || attempted`, and this is the
   * product's one model. Three were in use and none of them said why.
   *
   * - `touched` — the operator has been in this field and left it. They have
   *   finished their answer, so telling them it is wrong is an answer to a
   *   question they just asked; telling them *while they type* is correcting
   *   somebody mid-sentence.
   * - `attempted` — they pressed the button. Every rule the form is holding
   *   back is now owed to them at once, including on fields they never
   *   entered.
   *
   * And once shown, it stays shown until it is fixed: an error that hides
   * itself again on the next keystroke is a rule the operator has to
   * re-discover by being refused a second time.
   */
  const showSlugError = (slugTouched || attempted) && slugError

  const dirty =
    name !== "" ||
    slug !== "" ||
    repo !== "" ||
    icon !== "" ||
    color !== null ||
    tags.length > 0

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
      icon: icon.trim() || null,
      color,
      tags,
    })
  }

  return (
    <FormLayout data-test="create-project" onSubmit={submit}>
      <FormFields>
        <TextField
          id="project-name"
          label={t("form.nameLabel")}
          required
          autoFocus
          value={name}
          disabled={busy}
          placeholder={t("form.namePlaceholder")}
          onValueChange={(next) => {
            setName(next)
            if (!slugTouched) {
              setSlug(slugify(next))
            }
          }}
        />

        <TextField
          id="project-slug"
          label={t("form.slugLabel")}
          required
          value={slug}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder={t("form.slugPlaceholder")}
          hint={t("form.slugHint")}
          error={showSlugError ? slugError : null}
          onValueChange={(next) => {
            setSlugTouched(true)
            setSlug(next)
          }}
        />

        <TextField
          id="project-repo"
          label={t("identity.repo")}
          value={repo}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder={t("form.repoPlaceholder")}
          hint={t("form.repoHint")}
          onValueChange={setRepo}
        />

        <TextField
          id="project-icon"
          label={t("identity.icon")}
          value={icon}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder={t("identity.iconPlaceholder")}
          hint={t("form.iconHint")}
          onValueChange={setIcon}
        />

        <ColorField
          id="project-color"
          label={t("identity.accentColour")}
          value={color}
          onValueChange={setColor}
          clearLabel={t("form.colorClear")}
          disabled={busy}
          hint={t("form.colorHint")}
        />

        <TagEntryField
          id="project-tags"
          value={tags}
          onValueChange={setTags}
          disabled={busy}
          hint={t("form.tagsHint")}
        />
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="form-submit"
          denied={create.denial}
          loading={busy}
          disabled={name.trim().length === 0}
        >
          {t("form.submit")}
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
