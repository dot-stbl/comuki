import { useMemo, useState } from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  useIdentityQuery,
  useInviteUserMutation,
} from "@/domains/identity/api/queries"
import type { InviteUserInput } from "@/domains/identity/model/types"
import { InviteUserForm } from "@/domains/identity/ui/invite-user-form"
import { ConfirmDialog } from "@/shared/ui"

/**
 * Adding a person, on its own screen at `/identity/users/new`.
 *
 * Identity is three lists behind three tabs, so "back to the list" is a tab as
 * well as a path — `/identity?tab=users`. Creating an account lands there
 * narrowed to the address that was just written, so the operator sees the row
 * they made and can see, in the toolbar, why the list is one row long.
 */
export function InviteUserPage() {
  const navigate = useNavigate()
  const router = useRouter()
  const { data } = useIdentityQuery()
  const invite = useInviteUserMutation()

  const [dirty, setDirty] = useState(false)
  const guard = useUnsavedGuard(dirty)

  const takenAddresses = useMemo(
    () => (data?.users ?? []).map((user) => user.email.toLowerCase()),
    [data]
  )

  const cancel = () => {
    guard.leave(() => {
      if (router.history.canGoBack()) {
        router.history.back()
        return
      }
      void navigate({ to: "/identity", search: { tab: "users" } })
    })
  }

  const onInvite = (input: InviteUserInput) => {
    invite.mutate(input, {
      onSuccess: () => {
        toast.success(
          input.invite ? "Invitation sent" : "Local account created",
          {
            description: input.email,
          }
        )
        guard.leave(() => {
          void navigate({
            to: "/identity",
            search: { tab: "users", q: input.email },
            replace: true,
          })
        })
      },
    })
  }

  return (
    <FormPage
      title="New user"
      crumbs={[
        { label: "platform" },
        { label: "identity", to: "/identity" },
        { label: "new user" },
      ]}
      summary="An account can exist and hold nothing. Roles are granted separately, on the role assignments list."
    >
      <InviteUserForm
        takenAddresses={takenAddresses}
        busy={invite.isPending}
        onInvite={onInvite}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      <ConfirmDialog
        open={guard.asking}
        title="Leave without creating the account?"
        body="The name and address you typed are not saved anywhere yet. Leaving this page drops them."
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}
