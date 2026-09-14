import { useEffect, useMemo, useState } from "react"
import type { FormEvent, ReactNode } from "react"
import { Loader2, PlugZap } from "lucide-react"

import { FormActions, FormCard, FormLayout } from "@/app/layout/form-page"
import { effectiveAuth, needsBaseUrl } from "@/domains/sources/model/providers"
import type {
  ProbeResult,
  SourceAuth,
  SourceConnection,
} from "@/domains/sources/model/types"
import { ConnectionFields } from "@/domains/sources/ui/connection-fields"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, Notice, TextField, Tooltip } from "@/shared/ui"

// The domain's one spinner, shared with the row-level test and the create
// form's own probe so all three readings of "probing" are the same mark.
import tableStyles from "./sources-table.module.css"

// The probe's answer row, shared with the create form so the two screens read
// one control and one answer, drawn once.
import probeStyles from "./probe-row.module.css"

export interface ConnectionFormProps {
  connection: SourceConnection
  /** The page's standing probe, or `null` once an edit has dropped it. */
  probe: ProbeResult | null
  probing: boolean
  busy?: boolean
  /**
   * Fires the page's one probe — the stored connection, credential and all.
   *
   * There is still exactly one probe on the page; it lives here now, inside
   * the base url's box where there is one, because "the url and test it" is
   * one control. A second test button anywhere else would ask the provider
   * the same question and give the operator two places to read one answer.
   */
  onTest: () => void
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
 * The three shared questions themselves — the url, the credential kind,
 * the account — are `ConnectionFields`, the same component the create
 * form renders, so a rule about them cannot be true on one screen and
 * quietly false on the other.
 *
 * ## Test before save, on a connection that already exists
 *
 * The same discipline as `/sources/new`, for the same reason and with
 * one honest difference. The probe is the page's `useTestConnection`: it
 * reaches the instance this connection holds *now*, with the credential it
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
  onTest,
  onDraftChange,
  onSave,
  onDirtyChange,
}: ConnectionFormProps) {
  const session = useSession()

  const [auth, setAuth] = useState<SourceAuth>(connection.auth)
  const [account, setAccount] = useState(connection.account)
  const [baseUrl, setBaseUrl] = useState(connection.baseUrl ?? "")
  const [secretEnvRef, setSecretEnvRef] = useState(
    connection.secretEnvRef ?? ""
  )

  /* The row before the registry: a connection that already carries a base url
     keeps its box whatever the registry knows about its provider, which is the
     difference between editing an unknown provider's instance and losing it.
     A cloud jira has no `selfHosted` and still gets the box, because its
     provider can be self-hosted and the operator may be moving it. */
  const wantsHost = connection.selfHosted || needsBaseUrl(connection.kind)
  const storedBaseUrl = connection.baseUrl ?? ""
  const storedSecret = connection.secretEnvRef ?? ""

  /* Derived at render for the same reason the create form derives it: a row
     saved before this build learned its provider, or saved with a credential
     the connector has since dropped, must not sit selected-but-unreachable
     in a closed row. The first allowed kind stands in until the operator
     picks, and the save writes what the row offered. */
  const effective = effectiveAuth(connection.kind, auth)

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
      effective !== connection.auth ||
      account !== connection.account ||
      (wantsHost && baseUrl !== storedBaseUrl) ||
      secretEnvRef !== storedSecret,
    [
      effective,
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

  /* The record's own probe, in from the header where it used to live: the
     glyph rides inside the base url's box where there is one, and stands at
     the head of its answer where there is not. `denied` rather than
     `disabled` for the role, `disabled` for busy — an act refused to a role
     stays hoverable so its sentence is reachable. */
  const probeControl: ReactNode = (
    <Tooltip content={denied ?? "Test connection"}>
      <Button
        variant="outline"
        size="icon-sm"
        data-test="source-test"
        denied={denied}
        disabled={probing || busy}
        aria-busy={probing || undefined}
        aria-label={`Test the connection to ${connection.name}`}
        onClick={onTest}
      >
        {probing ? (
          <Loader2 className={tableStyles.spin} aria-hidden="true" />
        ) : (
          <PlugZap aria-hidden="true" />
        )}
      </Button>
    </Tooltip>
  )

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (denied || busy || !complete || !tested) {
      return
    }
    onSave({
      auth: effective,
      account: account.trim(),
      baseUrl: wantsHost ? baseUrl.trim() : "",
      secretEnvRef: secretEnvRef.trim(),
    })
  }

  return (
    <FormLayout data-test="connection-form" onSubmit={submit}>
      {/* The card the create form's groups taught this page: the region
          heading above is full width, and the form under it now spends that
          same width rather than sitting in a 44rem stack beside it. */}
      <FormCard
        label="the way in"
        note="where the instance is, which credential reaches it, and the env-var on the host that holds it."
      >
        <ConnectionFields
          idPrefix="connection"
          kind={connection.kind}
          auth={effective}
          baseUrl={baseUrl}
          account={account}
          wantsHost={wantsHost}
          disabled={busy}
          urlSuffix={probeControl}
          onBaseUrlChange={edit(setBaseUrl)}
          onAuthChange={edit(setAuth)}
          onAccountChange={edit(setAccount)}
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

        {/* The answer to the probe the form holds. One control, one answer:
            where the url box carried the control, this is the answer alone,
            landing under the field that produced it. */}
        <div className={probeStyles.probe} data-test="probe">
          {wantsHost ? null : (
            <span className={probeStyles.probeControl}>{probeControl}</span>
          )}

          <span className={probeStyles.probeAnswer}>
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
          </span>
        </div>
      </FormCard>

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
