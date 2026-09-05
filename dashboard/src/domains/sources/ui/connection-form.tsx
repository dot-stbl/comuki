import { useEffect, useMemo, useState } from "react"
import type { FormEvent } from "react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import type { SourceAuth, SourceConnection } from "@/domains/sources/model/types"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, Notice, SelectField, TextField } from "@/shared/ui"

export interface ConnectionFormProps {
  connection: SourceConnection
  /** The page's standing probe, or `null` once an edit has dropped it. */
  probe: import("@/domains/sources/model/types").ProbeResult | null
  probing: boolean
  busy?: boolean
  /** "The details moved — forget the last answer." */
  onDraftChange: () => void
  onSave: (patch: {
    auth: SourceAuth
    account: string
    baseUrl: string
    /** The new env-var name; the dashboard does not see the value. */
    secretEnvRef: string
  }) => void
  /** Tells the page whether there is anything here worth asking about. */
  onDirtyChange?: (dirty: boolean) => void
}

const SELF_HOSTED_KINDS: ReadonlySet<SourceConnection["kind"]> = new Set([
  "gitlab",
  "jira",
])

const AUTH_OPTIONS: {value: SourceAuth; label: string}[] = [
  {value: "pat", label: "personal access token"},
  {value: "oauth", label: "oauth grant"},
  {value: "app-install", label: "app install"},
]

/**
 * The connection itself: where the instance is, which credential reaches
 * it, and which env-var on the host holds that credential.
 *
 * Three or four fields, and the ones that are genuinely editable on
 * something that already exists. What a connection *points at* is not
 * among them — repointing a row at another repository is a different
 * connection wearing an old id — and neither is the project, because
 * `sources.edit` is granted per project and moving a row between two
 * of them is a permission question the form could not ask honestly.
 * The credential's VALUE is not here either: it was written once by the
 * form that took it, and replacing one is reconnecting rather than
 * editing. What the form DOES carry is the env-var name (`secretEnvRef`)
 * the host resolves at call time, and the per-provider non-secret
 * settings (`auth`, `account`, `baseUrl`) that fold into `settingsJson`.
 *
 * ## Test before save, on a connection that already exists
 *
 * The same discipline as `/sources/new`, for the same reason and with
 * one honest difference. The probe is `useTestConnection`: it reaches
 * the instance this connection holds *now*, with the credential it
 * already has, because there is no endpoint that would take a draft
 * and the stored secret at once. So what the answer means here is
 * "the way in still works" — and the rule is that you may not change
 * how something is reached until you know it can still be reached. A
 * save that lands on a connection that was already broken is a save
 * whose failure gets blamed on the edit.
 *
 * Any edit drops the answer, exactly as on the create form: an
 * instance that answered before the host was retyped is not evidence
 * about the host in the box now. `disabled` and not `denied` —
 * untested is invalid, not forbidden.
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

  const [auth, setAuth] = useState<SourceAuth>(connection.auth)
  const [account, setAccount] = useState(connection.account)
  const [baseUrl, setBaseUrl] = useState(connection.baseUrl ?? "")
  const [secretEnvRef, setSecretEnvRef] = useState(connection.secretEnvRef ?? "")

  const wantsHost = SELF_HOSTED_KINDS.has(connection.kind)
  const storedBaseUrl = connection.baseUrl ?? ""
  const storedSecret = connection.secretEnvRef ?? ""

  const denied = can(session, "sources.edit", connection.projectId)
    ? null
    : needsLabel("sources.edit", projectOf(session, connection.projectId)?.key)

  /* Measured against the stored connection rather than tracked with a flag: a
     save that lands makes the two agree, so the page's guard lets go by itself.
     The base url only counts where there is one to name — a cloud provider's
     box does not exist, so it cannot be dirty. The secret env ref counts
     whenever it changes; the stored reference is not shown back, but the
     dash is the operator's signal it changed. */
  const dirty = useMemo(
    () =>
      auth !== connection.auth ||
      account !== connection.account ||
      (wantsHost && baseUrl !== storedBaseUrl) ||
      secretEnvRef !== storedSecret,
    [
      auth,
      account,
      baseUrl,
      secretEnvRef,
      connection.auth,
      connection.account,
      storedBaseUrl,
      storedSecret,
      wantsHost,
    ]
  )

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
    account.trim().length > 0 &&
    secretEnvRef.trim().length > 0 &&
    (!wantsHost || baseUrl.trim().length > 0)
  const tested = probe?.ok === true

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (denied || busy || !complete || !tested) {
      return
    }
    onSave({
      auth,
      account: account.trim(),
      baseUrl: wantsHost ? baseUrl.trim() : "",
      secretEnvRef: secretEnvRef.trim(),
    })
  }

  return (
    <FormLayout data-test="connection-form" onSubmit={submit}>
      <FormFields>
        {wantsHost ? (
          <TextField
            id="connection-base-url"
            label="base url"
            value={baseUrl}
            disabled={busy}
            placeholder="https://git.example.internal"
            spellCheck={false}
            hint="self-hosted only. https, because the credential crosses this wire."
            data-test="connection-base-url"
            onValueChange={edit(setBaseUrl)}
          />
        ) : null}

        <SelectField
          id="connection-auth"
          label="auth kind"
          value={auth}
          disabled={busy}
          options={AUTH_OPTIONS}
          hint="what the connector accepts. Stored verbatim in the settings json; never holds a credential."
          data-test="connection-auth"
          onValueChange={(next: string) => edit(setAuth)(next as SourceAuth)}
        />

        <TextField
          id="connection-account"
          label="account"
          value={account}
          disabled={busy}
          placeholder="the bot or app the credential belongs to"
          spellCheck={false}
          hint="shown on the row afterwards, so a stale credential can be traced to a person."
          data-test="connection-account"
          onValueChange={edit(setAccount)}
        />

        <TextField
          id="connection-secret-env"
          label="secret env var"
          value={secretEnvRef}
          disabled={busy}
          placeholder="COMUKI_GITHUB_TOKEN"
          autoComplete="off"
          spellCheck={false}
          hint="the env-var name on the host. The dashboard never sees the value; the host resolves it at probe / webhook time."
          data-test="connection-secret-env"
          onValueChange={edit(setSecretEnvRef)}
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
            setAuth(connection.auth)
            setAccount(connection.account)
            setBaseUrl(storedBaseUrl)
            setSecretEnvRef(storedSecret)
          }}
        >
          Cancel
        </Button>
      </FormActions>
    </FormLayout>
  )
}