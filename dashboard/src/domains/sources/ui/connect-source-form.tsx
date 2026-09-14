import { useEffect, useState } from "react"
import type { FormEvent } from "react"
import { Loader2, PlugZap } from "lucide-react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import {
  CONNECTABLE_PROVIDERS,
  effectiveAuth,
  needsBaseUrl,
  targetLabel,
  targetPlaceholder,
} from "@/domains/sources/model/providers"
import type {
  ProbeResult,
  ProviderKey,
  SourceAuth,
} from "@/domains/sources/model/types"
import {
  type SecretReferenceDraft,
  type TestDraftInput,
} from "@/domains/sources/api/mutations"
import { ConnectionFields } from "@/domains/sources/ui/connection-fields"
import { providerCardOptions } from "@/domains/sources/ui/provider-card-options"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { env } from "@/shared/config/env"
import {
  Button,
  Notice,
  ProviderCards,
  SelectField,
  TextField,
  Tooltip,
} from "@/shared/ui"

// The domain's one spinner, shared with the row-level test and the source
// page's own probe so all three readings of "probing" are the same mark.
import tableStyles from "./sources-table.module.css"

import styles from "./connect-source-form.module.css"

export interface ConnectSourceFormProps {
  /** The last probe, or `null` when the details have changed since one. */
  probe: ProbeResult | null
  probing: boolean
  busy?: boolean
  onTest: (input: TestDraftInput) => void
  /** "The details moved — forget the last answer." */
  onDraftChange: () => void
  onCreate: (draft: SecretReferenceDraft) => void
  onCancel: () => void
  /** Tells the page whether there is anything here worth asking about. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Connect a source: three decisions, in the order they constrain each other.
 *
 * The provider, the project it feeds, and the env-var NAME holding the
 * outbound credential. The dashboard form never holds a plaintext
 * credential — only `secretEnvRef` (the env-var name) plus a JSON object
 * of non-secret settings (auth kind, account, base url). The host resolves
 * the secret at probe and webhook time.
 *
 * **Test before save is a requirement, not a nicety**, so it is wired as
 * one: the submit stays `disabled` until a probe has come back ok, and
 * any edit afterwards drops that answer, because a credential that
 * worked before the host was changed is not evidence about the host
 * that is there now. `disabled` and not `denied` — an untested form is
 * *invalid*, not forbidden, and the two states must not look alike.
 * The probe's glyph rides inside the base url's own box where there is
 * one, so "the url and test it" is one control; where the provider has
 * no instance to name, it stands at the head of its answer instead.
 *
 * The mock path still asks the operator to type a literal secret because
 * there is no env-var layer in mock mode; the literal value is held by
 * the form for exactly as long as the form is mounted, and never reaches
 * any other surface. Real mode never asks.
 */
export function ConnectSourceForm({
  probe,
  probing,
  busy = false,
  onTest,
  onDraftChange,
  onCreate,
  onCancel,
  onDirtyChange,
}: ConnectSourceFormProps) {
  const session = useSession()

  const [kind, setKind] = useState<ProviderKey>(CONNECTABLE_PROVIDERS[0].key)
  const [projectId, setProjectId] = useState(session.projects[0]?.id ?? "")
  const [name, setName] = useState("")
  const [auth, setAuth] = useState<SourceAuth>("pat")
  const [account, setAccount] = useState("")
  const [baseUrl, setBaseUrl] = useState("")
  /**
   * The env-var name that holds the credential. The dashboard never sees
   * the value; the host resolves it at probe and webhook time.
   */
  const [secretEnvRef, setSecretEnvRef] = useState("")
  /**
   * Mock-only literal credential, held by the form for exactly as long as
   * the form is mounted. Never sent on the wire — real mode reads the
   * env var from the host instead.
   */
  const [mockSecret, setMockSecret] = useState("")
  /* Coarse on purpose, the way `useUnsavedGuard` asks for it: any field touched
     at all. A form this short has no drafts worth diffing, and a guard that
     tried to be clever about which edits matter is a guard that will one day
     drop the one that did. */
  const [touched, setTouched] = useState(false)

  /* Derived at render, not corrected by an effect: changing the provider
     must not be able to leave the form holding a credential the new
     connector cannot use, and an effect that fixed it afterwards would
     fire in whatever order React felt like. See `effectiveAuth`. */
  const effective = effectiveAuth(kind, auth)
  const wantsHost = needsBaseUrl(kind)
  const draft: SecretReferenceDraft = {
    projectId,
    kind,
    name: name.trim(),
    auth: effective,
    account: account.trim(),
    baseUrl: wantsHost ? baseUrl.trim() : "",
    secretEnvRef: secretEnvRef.trim(),
  }
  const settingsJson = JSON.stringify({
    auth: effective,
    account: account.trim(),
    baseUrl: draft.baseUrl,
  })

  const projectKey = projectOf(session, projectId)?.key
  const denied = can(session, "sources.edit", projectId)
    ? null
    : needsLabel("sources.edit", projectKey)

  useEffect(() => {
    onDirtyChange?.(touched)
  }, [touched, onDirtyChange])

  // Edits invalidate the last answer. Wrapped once so no field can forget.
  const edit =
    <T,>(set: (next: T) => void) =>
    (next: T) => {
      set(next)
      setTouched(true)
      onDraftChange()
    }

  const complete =
    draft.name.length > 0 &&
    draft.account.length > 0 &&
    draft.secretEnvRef.length > 0 &&
    (!wantsHost || draft.baseUrl.length > 0) &&
    (!env.useMock || mockSecret.trim().length > 0)

  const tested = probe?.ok === true

  /* Two words became a glyph, so this is the same mark the row-level test
     wears and the tooltip carries the words it lost. `disabled` rather than
     `denied`: an incomplete draft is nothing to ask the provider about,
     which is invalid rather than forbidden. */
  const probeControl = (
    <Tooltip content="Test connection">
      <Button
        variant="outline"
        size="icon-sm"
        data-test="connect-test"
        disabled={!complete || probing || busy}
        aria-busy={probing || undefined}
        aria-label="Test connection"
        onClick={() =>
          onTest({
            draft,
            secretEnvRef: draft.secretEnvRef,
            mockSecret,
          })
        }
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
    onCreate(draft)
  }

  return (
    <FormLayout data-test="connect-source" onSubmit={submit}>
      <FormFields>
        <ProviderCards
          label="provider"
          name="connect-kind"
          value={kind}
          disabled={busy}
          options={providerCardOptions(CONNECTABLE_PROVIDERS)}
          hint="native intake is not here: every project already has one, and there is nothing to point a credential at."
          data-test="connect-kind"
          cardDataTest="connect-provider-card"
          onValueChange={edit(setKind)}
        />

        <SelectField
          id="connect-project"
          label="project"
          value={projectId}
          disabled={busy}
          options={session.projects.map((project) => ({
            value: project.id,
            label: project.name,
            secondary: project.key,
          }))}
          hint="the project this source feeds. Editing sources is granted per project, so this choice is what the save answers to."
          data-test="connect-project"
          onValueChange={edit(setProjectId)}
        />

        <TextField
          id="connect-name"
          label={targetLabel(kind)}
          value={name}
          disabled={busy}
          placeholder={targetPlaceholder(kind)}
          spellCheck={false}
          data-test="connect-name"
          onValueChange={edit(setName)}
        />

        <ConnectionFields
          idPrefix="connect"
          kind={kind}
          auth={effective}
          baseUrl={baseUrl}
          account={account}
          disabled={busy}
          urlSuffix={probeControl}
          onBaseUrlChange={edit(setBaseUrl)}
          onAuthChange={edit(setAuth)}
          onAccountChange={edit(setAccount)}
        />

        <Notice data-test="settings-preview">
          The host stores this as a single settings json:{" "}
          <code>{settingsJson}</code>
        </Notice>

        <TextField
          id="connect-secret-env"
          label="secret env var"
          value={secretEnvRef}
          disabled={busy}
          placeholder="COMUKI_GITHUB_TOKEN"
          autoComplete="off"
          spellCheck={false}
          hint="the name of the env var on the host that holds the credential. The host resolves the value at probe / webhook time — the dashboard never sees it."
          data-test="connect-secret-env"
          onValueChange={edit(setSecretEnvRef)}
        />

        {env.useMock ? (
          <>
            <Notice data-test="mock-secret-notice">
              Mock mode only: the form holds a literal credential long enough to
              probe the seed store. Real mode reads the env var on the host
              instead and never sees the value.
            </Notice>
            <TextField
              id="connect-mock-secret"
              label="credential (mock only)"
              type="password"
              value={mockSecret}
              disabled={busy}
              autoComplete="off"
              spellCheck={false}
              data-test="connect-mock-secret"
              onValueChange={edit(setMockSecret)}
            />
          </>
        ) : null}

        <div className={styles.probe} data-test="probe">
          {/* Where the provider has an instance to name, the probe's glyph
              rides inside the base url's box and does not stand here; where
              it does not, this row is where the operator finds it. Either
              way there is one control and one answer under it. */}
          {wantsHost ? null : (
            <span className={styles.probeControl}>{probeControl}</span>
          )}

          <span className={styles.probeAnswer}>
            {probe ? (
              <Notice tone={probe.ok ? "ok" : "bad"} data-test="probe-result">
                {probe.message}
              </Notice>
            ) : (
              <Notice tone="warn" data-test="probe-pending">
                {probing
                  ? "reaching the provider…"
                  : "test the connection before saving — an unreachable source looks exactly like a healthy one until something needs it."}
              </Notice>
            )}
          </span>
        </div>
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="form-submit"
          denied={denied}
          disabled={busy || !complete || !tested}
          aria-busy={busy || undefined}
        >
          Save connection
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
