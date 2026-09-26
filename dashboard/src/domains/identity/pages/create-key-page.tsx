import { useState } from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import { useCreateApiKeyMutation } from "@/domains/identity/api/queries"
import type { CreateApiKeyInput } from "@/domains/identity/model/types"
import { CreateKeyForm } from "@/domains/identity/ui/create-key-form"
import {
  KeySecretDialog,
  type CreatedKey,
} from "@/domains/identity/ui/key-secret-dialog"
import { requestFailureMessage } from "@/shared/api/problem"
import { ConfirmDialog, Notice } from "@/shared/ui"

/**
 * Making an api key, at `/identity/keys/new` — and the one place in this
 * product where a secret is ever on a screen.
 *
 * **The plaintext lives in this component's state and nowhere else.** It
 * arrives as the mutation's return value, is copied into `created`, and the
 * mutation is reset in the same breath so its cache is not a second holder —
 * otherwise a secret would sit in the query client for the rest of the session,
 * reachable by anything that re-rendered. It is never put into the path, the
 * query string or the router's location state, so there is no address that
 * renders it at all, let alone twice: reloading this URL gives an empty form.
 * `key-secret-dialog.tsx` carries the full argument for why the showing stayed
 * a dialog while the form around it became a page.
 *
 * Closing the dialog drops the state and leaves for the key list. There is no
 * path back to the value — the store kept only the prefix.
 */
export function CreateKeyPage() {
  const navigate = useNavigate()
  const router = useRouter()
  const { t } = useTranslation("identity")
  const { t: tShell } = useTranslation("shell")
  const createKey = useCreateApiKeyMutation()

  const [dirty, setDirty] = useState(false)
  const [created, setCreated] = useState<CreatedKey | null>(null)
  const guard = useUnsavedGuard(dirty && created === null)

  const toKeys = (q?: string) => {
    guard.leave(() => {
      void navigate({
        to: "/identity",
        search: { tab: "keys", q },
        replace: true,
      })
    })
  }

  const cancel = () => {
    guard.leave(() => {
      if (router.history.canGoBack()) {
        router.history.back()
        return
      }
      void navigate({ to: "/identity", search: { tab: "keys" } })
    })
  }

  const onCreate = (input: CreateApiKeyInput) => {
    createKey.mutate(input, {
      onSuccess: (result) => {
        setCreated(result)
        // One holder, immediately. `createKey.data` would otherwise keep the
        // plaintext alive in the query client for the rest of the session.
        createKey.reset()
      },
    })
  }

  const done = () => {
    const prefix = created?.prefix
    // The showing is over. There is nothing to restore it from.
    setCreated(null)
    toKeys(prefix)
  }

  return (
    <FormPage
      title={t("keyPage.title")}
      crumbs={[
        { label: tShell("crumb.platform") },
        { label: tShell("crumb.identity"), to: "/identity" },
        { label: t("keyPage.crumb") },
      ]}
      summary={t("keyPage.summary")}
    >
      {/* A refusal is the one answer this page could previously give in
          silence, and the silence read exactly like the success it is not:
          no dialog, no secret, no sentence. The host's own problem detail
          says why — the transport's status line would not. */}
      {createKey.error ? (
        <Notice tone="bad" data-test="key-failure">
          {requestFailureMessage(createKey.error, t("keyPage.refused"))}{" "}
          {t("keyPage.tail")}
        </Notice>
      ) : null}

      <CreateKeyForm
        busy={createKey.isPending}
        onCreate={onCreate}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      <KeySecretDialog created={created} onDone={done} />

      {/* Only ever asked before the key exists. Once the secret is on screen
          the form's contents are beside the point — the act is done, and the
          only question left is whether the operator has copied the value. */}
      <ConfirmDialog
        open={guard.asking}
        title={t("keyPage.leaveTitle")}
        body={t("keyPage.leaveBody")}
        confirmLabel={t("keyPage.discard")}
        cancelLabel={t("keyPage.keep")}
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}
