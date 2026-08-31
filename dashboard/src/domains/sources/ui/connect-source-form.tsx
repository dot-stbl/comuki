import { useEffect, useState } from "react"
import type { FormEvent } from "react"
import { Loader2, PlugZap } from "lucide-react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import {
  CONNECTABLE_KINDS,
  SOURCE_KIND_LABEL,
  effectiveAuth,
  needsBaseUrl,
  secretLabel,
  targetLabel,
  targetPlaceholder,
} from "@/domains/sources/model/providers"
import type {
  ProbeResult,
  SourceAuth,
  SourceKind,
} from "@/domains/sources/model/types"
import { ConnectionFields } from "@/domains/sources/ui/connection-fields"
import type { SeedSourceDraft } from "@/shared/api/mock/sources.store"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, Notice, SelectField, TextField, Tooltip } from "@/shared/ui"

// The domain's one spinner, shared with the row-level test and the source
// page's own probe so all three readings of "probing" are the same mark.
import tableStyles from "./sources-table.module.css"

import styles from "./connect-source-form.module.css"

export interface ConnectSourceFormProps {
  /** The last probe, or `null` when the details have changed since one. */
  probe: ProbeResult | null
  probing: boolean
  busy?: boolean
  onTest: (input: { draft: SeedSourceDraft; secret: string }) => void
  /** "The details moved — forget the last answer." */
  onDraftChange: () => void
  onCreate: (draft: SeedSourceDraft) => void
  onCancel: () => void
  /** Tells the page whether there is anything here worth asking about. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Connect a source: four decisions, in the order they constrain each other.
 *
 * The provider, the project it feeds, where the instance is, and the
 * credential. This was a dialog and is now the fields half of a page — the
 * arguments are unchanged, because none of them were about being in a modal.
 *
 * **Test before save is a requirement, not a nicety**, so it is wired as one:
 * the submit stays `disabled` until a probe has come back ok, and any edit
 * afterwards drops that answer, because a credential that worked before the
 * host was changed is not evidence about the host that is there now.
 * `disabled` and not `denied` — an untested form is *invalid*, not forbidden,
 * and the two states must not look alike.
 *
 * **The secret is said once, where it is entered.** The notice sits directly
 * above the box rather than under the button, because an irreversible rule
 * explained afterwards is an apology. Nothing downstream of this form holds the
 * value: it goes to the probe, it goes nowhere else, and the row that appears
 * afterwards carries a date and not a token.
 *
 * No router, no shell, no mutation — the page above owns all three, which is
 * what lets the fields and the rules about them be tested on their own. What it
 * *does* read is the session, because the act it gates on is `sources.edit` on
 * **the project this form picked**, and no one above it knows which that is
 * until the operator has chosen.
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

  const [kind, setKind] = useState<SourceKind>("github")
  const [projectId, setProjectId] = useState(session.projects[0]?.id ?? "")
  const [name, setName] = useState("")
  const [auth, setAuth] = useState<SourceAuth>("pat")
  const [account, setAccount] = useState("")
  const [baseUrl, setBaseUrl] = useState("")
  // The credential lives here, for exactly as long as this form is mounted.
  const [secret, setSecret] = useState("")
  /* Coarse on purpose, the way `useUnsavedGuard` asks for it: any field touched
     at all. A form this short has no drafts worth diffing, and a guard that
     tried to be clever about which edits matter is a guard that will one day
     drop the one that did. */
  const [touched, setTouched] = useState(false)

  // Derived rather than synced: changing the provider cannot leave the form
  // holding a credential kind that provider does not implement, and there is
  // no effect to fire in the wrong order.
  const chosenAuth = effectiveAuth(kind, auth)
  const wantsHost = needsBaseUrl(kind)

  const draft: SeedSourceDraft = {
    projectId,
    kind,
    name: name.trim(),
    auth: chosenAuth,
    account: account.trim(),
    baseUrl: wantsHost ? baseUrl.trim() : "",
  }

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
    secret.trim().length > 0 &&
    (!wantsHost || draft.baseUrl.length > 0)

  const tested = probe?.ok === true

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
        <SelectField
          id="connect-kind"
          label="provider"
          value={kind}
          disabled={busy}
          options={CONNECTABLE_KINDS.map((entry) => ({
            value: entry,
            label: SOURCE_KIND_LABEL[entry],
          }))}
          hint="native intake is not here: every project already has one, and there is nothing to point a credential at."
          data-test="connect-kind"
          onValueChange={edit((next: string) => setKind(next as SourceKind))}
        />

        <SelectField
          id="connect-project"
          label="project"
          value={projectId}
          disabled={busy}
          options={session.projects.map((project) => ({
            value: project.id,
            label: `${project.key} · ${project.name}`,
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
          baseUrl={baseUrl}
          account={account}
          auth={chosenAuth}
          disabled={busy}
          onBaseUrlChange={edit(setBaseUrl)}
          onAuthChange={edit(setAuth)}
          onAccountChange={edit(setAccount)}
        />

        <Notice data-test="secret-notice">
          The {secretLabel(chosenAuth)} is stored write-only. It is never shown
          again — not on this row, not in this form, not through the api — so
          keep your own copy before you save.
        </Notice>

        <TextField
          id="connect-secret"
          label={secretLabel(chosenAuth)}
          type="password"
          value={secret}
          disabled={busy}
          autoComplete="off"
          spellCheck={false}
          data-test="connect-secret"
          onValueChange={edit(setSecret)}
        />

        <div className={styles.probe}>
          {/* Two words became a glyph, so this is the same mark the row-level
              test wears and the tooltip carries the words it lost. `disabled`
              rather than `denied`: an incomplete draft is nothing to ask the
              provider about, which is invalid rather than forbidden. */}
          <span className={styles.probeControl}>
            <Tooltip content="Test connection">
              <Button
                variant="outline"
                size="icon"
                data-test="connect-test"
                disabled={!complete || probing || busy}
                aria-busy={probing || undefined}
                aria-label="Test connection"
                onClick={() => onTest({ draft, secret })}
              >
                {probing ? (
                  <Loader2 className={tableStyles.spin} aria-hidden="true" />
                ) : (
                  <PlugZap aria-hidden="true" />
                )}
              </Button>
            </Tooltip>
          </span>

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
