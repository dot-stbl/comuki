import { useState } from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  useGrantRoleMutation,
  useIdentityQuery,
} from "@/domains/identity/api/queries"
import type { GrantRoleInput } from "@/domains/identity/model/types"
import { GrantRoleForm } from "@/domains/identity/ui/grant-role-form"
import { ConfirmDialog } from "@/shared/ui"

/**
 * Writing a grant, on its own screen at `/identity/grants/new`.
 *
 * The form needs three lists to offer its four choices — users, keys and
 * projects — which is precisely why this reads the same one-payload snapshot
 * the list screen does rather than fetching its own. Two queries would let the
 * form offer a subject the list had already revoked.
 */
export function GrantRolePage() {
  const navigate = useNavigate()
  const router = useRouter()
  const { data } = useIdentityQuery()
  const grantRole = useGrantRoleMutation()

  const [dirty, setDirty] = useState(false)
  const guard = useUnsavedGuard(dirty)

  const users = data?.users ?? []
  const keys = data?.keys ?? []
  const projects = data?.projects ?? []

  const cancel = () => {
    guard.leave(() => {
      if (router.history.canGoBack()) {
        router.history.back()
        return
      }
      void navigate({ to: "/identity", search: { tab: "grants" } })
    })
  }

  const onGrant = (input: GrantRoleInput) => {
    const scope =
      projects.find((project) => project.id === input.projectId)?.slug ??
      "platform"
    const subject =
      input.subjectKind === "user"
        ? users.find((user) => user.id === input.subjectId)?.email
        : keys.find((key) => key.id === input.subjectId)?.prefix

    grantRole.mutate(input, {
      onSuccess: () => {
        toast.success("Role granted", {
          description: `${input.role} on ${scope}`,
        })
        guard.leave(() => {
          // Narrowed to the subject rather than to the role: a person holds
          // several grants and the new one is read next to the others they
          // already had, which is the question an administrator actually has.
          void navigate({
            to: "/identity",
            search: { tab: "grants", q: subject },
            replace: true,
          })
        })
      },
    })
  }

  return (
    <FormPage
      title="Grant a role"
      crumbs={[
        { label: "platform" },
        { label: "identity", to: "/identity" },
        { label: "grant a role" },
      ]}
      summary="A grant is a subject, a role and a scope. Nothing else is stored, and nothing else is offered."
    >
      <GrantRoleForm
        users={users}
        keys={keys}
        projects={projects}
        busy={grantRole.isPending}
        onGrant={onGrant}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      <ConfirmDialog
        open={guard.asking}
        title="Leave without granting the role?"
        body="Nothing has been written yet. Leaving this page drops the subject, role and scope you chose."
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}
