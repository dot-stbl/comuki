import { useEffect, useState } from "react"
import type { FormEvent } from "react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import { effectiveAuth, needsBaseUrl } from "@/domains/sources/model/providers"
import type {
  ProbeResult,
  SourceAuth,
  SourceConnection,
} from "@/domains/sources/model/types"
import { ConnectionFields } from "@/domains/sources/ui/connection-fields"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, Notice } from "@/shared/ui"

export interface ConnectionFormProps {
  connection: SourceConnection
  /** The page's standing probe, or `null` once an edit has dropped it. */
  probe: ProbeResult | null
  probing: boolean
  busy?: boolean
  /** "The details moved — forget the last answer." */
  onDraftChange: () => void
  onSave: (patch: {
    baseUrl: string
    account: string
    auth: SourceAuth
  }) => void
  /** Tells the page whether there is anything here worth asking about. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * The connection itself: where the instance is, and which credential reaches
 * it.
 *
 * Three fields, and the three that are genuinely editable on something that
 * already exists. What a connection *points at* is not among them — repointing
 * a row at another repository is a different connection wearing an old id — and
 * neither is the project, because `sources.edit` is granted per project and
 * moving a row between two of them is a permission question the form could not
 * ask honestly. The credential is not here either: it was written once by the
 * form that took it, and replacing one is reconnecting.
 *
 * ## Test before save, on a connection that already exists
 *
 * The same discipline as `/sources/new`, for the same reason and with one
 * honest difference. The probe is `useTestConnection`: it reaches the instance
 * this connection holds *now*, with the credential it already has, because
 * there is no endpoint that would take a draft and the stored secret at once.
 * So what the answer means here is "the way in still works" — and the rule is
 * that you may not change how something is reached until you know it can still
 * be reached. A save that lands on a connection that was already broken is a
 * save whose failure gets blamed on the edit.
 *
 * Any edit drops the answer, exactly as on the create form: an instance that
 * answered before the host was retyped is not evidence about the host in the
 * box now. `disabled` and not `denied` — untested is invalid, not forbidden.
 *
 * Recorded rather than papered over: a connection in `error` cannot be saved
 * from here at all, because its probe never comes back ok. That is the right
 * answer today — a revoked token is fixed by reconnecting with a new one, which
 * is what its own reason says on the row — and the day the endpoint takes a
 * draft, this form hands it one.
 */
export function ConnectionForm({
  connection,
  probe,
  probing,
  busy = false,
  onDraftChange,
  onSave,
  onDirtyChange,
}: ConnectionFormProps) {
  const session = useSession()

  const [baseUrl, setBaseUrl] = useState(connection.baseUrl ?? "")
  const [account, setAccount] = useState(connection.account)
  const [auth, setAuth] = useState<SourceAuth>(connection.auth)

  const chosenAuth = effectiveAuth(connection.kind, auth)
  const wantsHost = needsBaseUrl(connection.kind)
  const storedBaseUrl = connection.baseUrl ?? ""

  const denied = can(session, "sources.edit", connection.projectId)
    ? null
    : needsLabel("sources.edit", projectOf(session, connection.projectId)?.key)

  /* Measured against the stored connection rather than tracked with a flag: a
     save that lands makes the two agree, so the page's guard lets go by itself.
     The base url only counts where there is one to name — a cloud provider's
     box does not exist, so it cannot be dirty. */
  const dirty =
    account !== connection.account ||
    chosenAuth !== connection.auth ||
    (wantsHost && baseUrl !== storedBaseUrl)

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const edit =
    <T,>(set: (next: T) => void) =>
    (next: T) => {
      set(next)
      onDraftChange()
    }

  const complete =
    account.trim().length > 0 && (!wantsHost || baseUrl.trim().length > 0)
  const tested = probe?.ok === true

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (denied || busy || !complete || !tested) {
      return
    }
    onSave({
      baseUrl: wantsHost ? baseUrl.trim() : "",
      account: account.trim(),
      auth: chosenAuth,
    })
  }

  return (
    <FormLayout data-test="connection-form" onSubmit={submit}>
      <FormFields>
        <ConnectionFields
          idPrefix="connection"
          kind={connection.kind}
          baseUrl={baseUrl}
          account={account}
          auth={chosenAuth}
          disabled={busy}
          onBaseUrlChange={edit(setBaseUrl)}
          onAuthChange={edit(setAuth)}
          onAccountChange={edit(setAccount)}
        />

        {/* The answer to the probe the header holds. One control, one answer:
            a second test button down here would ask the provider the same
            question and give the operator two places to read it. */}
        {probe ? (
          <Notice tone={probe.ok ? "ok" : "bad"} data-test="probe-result">
            {probe.message}
          </Notice>
        ) : (
          <Notice tone="warn" data-test="probe-pending">
            {probing
              ? "reaching the provider…"
              : "test the connection before saving — changing how a source is reached is not something to do while nobody knows whether it can be."}
          </Notice>
        )}
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="connection-submit"
          denied={denied}
          disabled={busy || !complete || !tested}
          aria-busy={busy || undefined}
        >
          Save connection
        </Button>
        <Button
          variant="secondary"
          data-test="connection-cancel"
          disabled={busy}
          onClick={() => {
            setBaseUrl(storedBaseUrl)
            setAccount(connection.account)
            setAuth(connection.auth)
          }}
        >
          Cancel
        </Button>
      </FormActions>
    </FormLayout>
  )
}
