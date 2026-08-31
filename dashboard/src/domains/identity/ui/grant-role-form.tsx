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

  const blocked = !subject || (onProject && !project)

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
            value={subject}
            disabled={busy || subjects.length === 0}
            options={subjects}
            hint={
              subjects.length === 0
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
              value={project}
              disabled={busy || projectOptions.length === 0}
              options={projectOptions}
              hint={
                projectOptions.length === 0
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
          disabled={busy || blocked}
          aria-busy={busy || undefined}
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
