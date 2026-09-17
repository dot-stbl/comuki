import { useEffect, useState } from "react"
import type { FormEvent } from "react"

import {
  FormActions,
  FormFields,
  FormLayout,
  FormRow,
} from "@/app/layout/form-page"
import type {
  ApiKeyRow,
  GrantRoleInput,
  SubjectKind,
  UserRow,
} from "@/domains/identity/model/types"
import { ROLES, useCan, type Role } from "@/shared/session"
import { Button, SelectField } from "@/shared/ui"

const KINDS = [
  { value: "user", label: "user" },
  { value: "api-key", label: "api key" },
]

const SCOPES = [
  { value: "platform", label: "platform" },
  { value: "project", label: "a project" },
]

export interface GrantRoleFormProps {
  users: readonly UserRow[]
  keys: readonly ApiKeyRow[]
  projects: ReadonlyArray<{ id: string; slug: string; name: string }>
  /**
   * The three lists are still on their way.
   *
   * It matters because the hints below say "nothing of that kind to grant to
   * yet", and an empty array on its way and an empty array that is the answer
   * look identical from here — so a direct arrival on this URL used to be told
   * the platform held no users at all. The form has no error state to tell
   * apart from this one: the page above owns that.
   */
  loading?: boolean
  busy?: boolean
  onGrant: (input: GrantRoleInput) => void
  onCancel: () => void
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Subject, role, scope. There is no fourth field and there is no fifth screen.
 *
 * **A role cannot be created.** The six live in code, as a matrix of acts, and
 * the database holds only the fact that somebody was given one — so this form
 * offers exactly `ROLES` and there is no affordance anywhere in this product
 * that would add a seventh. The field says so in its own hint rather than
 * leaving the administrator to discover it by looking for a button that is not
 * there; a rule that is only enforced is a rule that reads as an omission.
 *
 * The scope pair is two controls rather than one flattened list because they
 * are two different decisions: *platform or a project* is the question, and
 * *which project* only exists once the first is answered. A single select
 * mixing `platform` in with four slugs would put a decision about the shape of
 * the grant next to a decision about its target. On a page they sit on one
 * line, which is what the room bought: they are one decision read left to
 * right rather than two stacked questions.
 */
export function GrantRoleForm({
  users,
  keys,
  projects,
  loading = false,
  busy = false,
  onGrant,
  onCancel,
  onDirtyChange,
}: GrantRoleFormProps) {
  const manage = useCan("identity.manage")

  const [kind, setKind] = useState<SubjectKind>("user")
  const [subjectId, setSubjectId] = useState("")
  const [role, setRole] = useState<Role>("viewer")
  const [scope, setScope] = useState("platform")
  const [projectId, setProjectId] = useState("")

  // A disabled account and a revoked key can still hold a grant — the seeds
  // have one of each — but neither is something to hand a *new* one to. The
  // list of things an act can happen to is filtered; the act itself is not.
  const subjects =
    kind === "user"
      ? users
          .filter((user) => user.status !== "disabled")
          .map((user) => ({ value: user.id, label: user.email }))
      : keys
          .filter((key) => key.status === "active")
          .map((key) => ({
            value: key.id,
            label: `${key.prefix} · ${key.name}`,
          }))

  const projectOptions = projects.map((project) => ({
    value: project.id,
    label: project.slug,
  }))

  // Derived rather than synced by an effect: props arrive after the first
  // render, and a state that has to be told about it is a state that will one
  // day not be told.
  const subject = subjectId || subjects[0]?.value || ""
  const project = projectId || projectOptions[0]?.value || ""
  const onProject = scope === "project"

  // Every field on this form has a working default, so "dirty" is anything
  // moved off one — there is no half-typed value to lose, only a decision.
  const dirty =
    kind !== "user" ||
    subjectId !== "" ||
    role !== "viewer" ||
    scope !== "platform" ||
    projectId !== ""

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  // Nothing can be written against a list that has not arrived either, so the
  // wait gates the submit exactly as an empty list does.
  const blocked = loading || !subject || (onProject && !project)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (manage.denial || busy || blocked) {
      return
    }
    onGrant({
      subjectKind: kind,
      subjectId: subject,
      role,
      projectId: onProject ? project : null,
    })
  }

  return (
    <FormLayout data-test="grant-role" onSubmit={submit}>
      <FormFields>
        <FormRow>
          <SelectField
            id="grant-kind"
            label="subject kind"
            value={kind}
            disabled={busy}
            options={KINDS}
            onValueChange={(next) => {
              setKind(next as SubjectKind)
              // The subject list changes entirely, so the held id is
              // meaningless.
              setSubjectId("")
            }}
          />
          <SelectField
            id="grant-subject"
            label="subject"
            required
            value={subject}
            disabled={busy || loading || subjects.length === 0}
            options={subjects}
            /* Three readings, not two: still coming, genuinely nothing, and a
               list. Collapsing the first into the second is what made a slow
               payload say the platform was empty. */
            hint={
              loading
                ? "Looking up what can hold a role."
                : subjects.length === 0
                  ? "Nothing of that kind to grant to yet."
                  : undefined
            }
            onValueChange={setSubjectId}
          />
        </FormRow>

        <SelectField
          id="grant-role"
          label="role"
          value={role}
          disabled={busy}
          options={ROLES.map((entry) => ({ value: entry, label: entry }))}
          hint="Roles live in code — these six are the whole set, and there is no way to add one."
          onValueChange={(next) => setRole(next as Role)}
        />

        <FormRow>
          <SelectField
            id="grant-scope"
            label="scope"
            value={scope}
            disabled={busy}
            options={SCOPES}
            hint="A platform grant holds everywhere. A project grant holds on one project and nowhere else."
            onValueChange={setScope}
          />
          {onProject ? (
            <SelectField
              id="grant-project"
              label="project"
              /* Only while the scope is a project — which is the only time
                 this field is rendered and the only time it gates the
                 submit. */
              required
              value={project}
              disabled={busy || loading || projectOptions.length === 0}
              options={projectOptions}
              hint={
                loading
                  ? "Looking up which projects a grant can be scoped to."
                  : projectOptions.length === 0
                    ? "No projects to scope a grant to yet."
                    : undefined
              }
              onValueChange={setProjectId}
            />
          ) : null}
        </FormRow>
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="form-submit"
          denied={manage.denial}
          loading={busy}
          disabled={blocked}
        >
          Grant
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
