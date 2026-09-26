import { useState } from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  useGrantRoleMutation,
  useIdentityQuery,
} from "@/domains/identity/api/queries"
import type { GrantRoleInput } from "@/domains/identity/model/types"
import { GrantRoleForm } from "@/domains/identity/ui/grant-role-form"
import { requestFailureMessage } from "@/shared/api/problem"
import { ConfirmDialog, Notice } from "@/shared/ui"

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
  const { t } = useTranslation("identity")
  const { t: tShell } = useTranslation("shell")
  const { data, isLoading } = useIdentityQuery()
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
        toast.success(t("grantPage.toast"), {
          description: t("grantPage.toastScope", { role: input.role, scope }),
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
      title={t("grantPage.title")}
      crumbs={[
        { label: tShell("crumb.platform") },
        { label: tShell("crumb.identity"), to: "/identity" },
        { label: t("grantPage.crumb") },
      ]}
      summary={t("grantPage.summary")}
    >
      {/* The host's own sentence, not `error.message`: a grant the platform
          refused says why in its problem body, and a screen that printed the
          transport's status line would be hiding the only reading the
          operator can act on. */}
      {grantRole.error ? (
        <Notice tone="bad" data-test="grant-failure">
          {requestFailureMessage(grantRole.error, t("grantPage.refused"))}{" "}
          {t("grantPage.tail")}
        </Notice>
      ) : null}

      <GrantRoleForm
        users={users}
        keys={keys}
        projects={projects}
        loading={isLoading}
        busy={grantRole.isPending}
        onGrant={onGrant}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      <ConfirmDialog
        open={guard.asking}
        title={t("grantPage.leaveTitle")}
        body={t("grantPage.leaveBody")}
        confirmLabel={t("grantPage.discard")}
        cancelLabel={t("grantPage.keep")}
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}
