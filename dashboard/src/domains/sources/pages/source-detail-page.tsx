import { useState } from "react"
import { ArrowLeft, Loader2, PlugZap, RotateCw, Unplug } from "lucide-react"
import { Link, useNavigate } from "@tanstack/react-router"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  useAdmissionRules,
  useDisconnectSource,
  useSaveWatch,
  useTestConnection,
  useUpdateConnection,
} from "@/domains/sources/api/mutations"
import { useSourcesQuery } from "@/domains/sources/api/queries"
import {
  AUTH_LABEL,
  NATIVE_DISCONNECT_REFUSAL,
  SOURCE_KIND_BRAND,
  SOURCE_KIND_LABEL,
  admittedCount,
  connectionHost,
  connectionNote,
} from "@/domains/sources/model/providers"
import type {
  AdmissionMode,
  ProbeResult,
  SourceAuth,
  SourceWatch,
} from "@/domains/sources/model/types"
import type { AdmissionRuleView } from "@/shared/api/_generated/types/AdmissionRuleView"
import { ConnectionForm } from "@/domains/sources/ui/connection-form"
import { ConnectionStateBadge } from "@/domains/sources/ui/connection-state-badge"
import { StatusMappingPreview } from "@/domains/sources/ui/status-mapping-preview"
import { WatchForm } from "@/domains/sources/ui/watch-form"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import {
  BrandTag,
  Button,
  ConfirmDialog,
  Notice,
  Section,
  Tooltip,
  buttonClass,
} from "@/shared/ui"

// The domain's one spinner, shared with the row-level test so the two readings
// of "probing" are the same mark.
import tableStyles from "@/domains/sources/ui/sources-table.module.css"

import styles from "./source-detail-page.module.css"

const SKELETON_WIDTHS = ["48%", "76%", "38%", "64%", "52%"]

/**
 * The host's `AdmissionRuleView` carries `mode` as the 2-mode string
 * (`watch` | `inbox`); the dashboard's `SourceWatch` carries the 3-mode
 * vocabulary (`watch` | `inbox-only` | `both`). Mapping back to the
 * dashboard side: `watch` lands as `watch` (or `both`, since both behave
 * the same on the host — start a run, ticket stays in the catalog);
 * `inbox` lands as `inbox-only`. The dashboard's read is deliberately
 * permissive — `both` shows the same form values regardless of which
 * host-mode word the rule was last saved as.
 *
 * `matched` and `mapping` are not on the wire today; the watch form
 * renders the upstream's preview locally and the count reads `0` until
 * the host grows a `/api/v1/sources/{id}/status` endpoint.
 */
function admissionRuleToWatch(rule: AdmissionRuleView): SourceWatch {
  const mode: AdmissionMode = rule.mode === "inbox" ? "inbox-only" : "watch"
  return {
    enabled: rule.enabled,
    mode,
    filter: rule.filterJson,
    matched: 0,
    mapping: [],
  }
}

export interface SourceDetailPageProps {
  /** From the path. A connection is a thing, so configuring one has an address. */
  sourceId: string
}

/**
 * One source connection, and everything that is decided about it.
 *
 * Two of the section's three dialogs are gone into this page, because both were
 * *configuration of a connection* and this product's standing rule is that
 * editing gets a page rather than a modal:
 *
 * - **Watch and filter** is the form in the second region. What it admits and
 *   who moves next were never a question about a row in a list; they are a
 *   question about this connection, and they need the room to say why.
 * - **Connect a source** split in two. Its create half is `/sources/new`; its
 *   *edit* half — the instance, the credential kind, the account — is the third
 *   region here, sharing the same field set so the two cannot drift.
 *
 * The third dialog did not fold in and should not have: filing a ticket is the
 * creation of a different entity, gated on a different permission, and it went
 * to `/sources/$sourceId/ticket/new` instead.
 *
 * ## What is up in the header and what is down in a footer
 *
 * The badge, the probe and the disconnect act on **the record**. The two
 * footers act on **a draft**. Mixing them would put "disconnect" beside "save",
 * and disconnecting is not a way of saving something. So the record's acts ride
 * in `PageHeader`'s actions slot, where they are visible whichever form is
 * being filled in.
 *
 * There is exactly one probe on this screen, in the header, and the connection
 * form reads its answer. A second test button beside the save would ask the
 * provider the same question and give the operator two places to read one
 * answer.
 *
 * ## What this page is not
 *
 * A detail page links to the real screens with a filter applied; it does not
 * redraw their tables. There is no ticket list here and no runs table — what
 * this connection admitted is a **count and a link**, because the catalog
 * already has a screen and a second copy of it is a second place for the same
 * rows to disagree.
 */
export function SourceDetailPage({ sourceId }: SourceDetailPageProps) {
  const navigate = useNavigate()
  const session = useSession()
  const { data, isLoading, isError, error, refetch } = useSourcesQuery()

  const testConnection = useTestConnection()
  const disconnect = useDisconnectSource()
  const saveWatch = useSaveWatch()
  const updateConnection = useUpdateConnection()

  /** The header probe's last answer, dropped the moment a detail changes. */
  const [probe, setProbe] = useState<ProbeResult | null>(null)
  const [watchDirty, setWatchDirty] = useState(false)
  const [connectionDirty, setConnectionDirty] = useState(false)
  const [disconnecting, setDisconnecting] = useState(false)

  // One guard over both forms: the question the operator is being asked is
  // "you typed something on this page and are leaving it", and which of the two
  // regions they typed in is not a distinction worth two dialogs.
  const guard = useUnsavedGuard(watchDirty || connectionDirty)

  const connection =
    data?.connections.find((entry) => entry.id === sourceId) ?? null
  const tickets = data?.tickets ?? []

  // The host's admission rules are siblings of source connections, not a
  // nested field — the watch form joins the matching rule by projectId on
  // the client side and writes back through the rule id (issue #40).
  const admissionRules = useAdmissionRules(connection?.projectId)

  const crumbs = [
    { label: "configure" },
    { label: "sources", to: "/sources" },
    { label: connection?.name ?? "source" },
  ]

  if (isLoading) {
    return (
      <FormPage title="Source" crumbs={crumbs}>
        <div className={styles.skeleton} data-test="source-loading">
          {SKELETON_WIDTHS.map((width, index) => (
            <span key={index} className={styles.skeletonBar} style={{ width }} />
          ))}
        </div>
      </FormPage>
    )
  }

  if (isError) {
    return (
      <FormPage title="Source" crumbs={crumbs}>
        <div className={styles.state} role="alert">
          <p className={styles.stateTitle}>Couldn&apos;t load this source</p>
          <p className={styles.stateBody}>
            {error instanceof Error ? error.message : "Unknown error"}
          </p>
          <span>
            <Tooltip content="Retry">
              <Button
                size="icon-sm"
                data-test="source-retry"
                aria-label="Retry"
                onClick={() => {
                  void refetch()
                }}
              >
                <RotateCw aria-hidden="true" />
              </Button>
            </Tooltip>
          </span>
        </div>
      </FormPage>
    )
  }

  if (!connection) {
    /* An address that names a connection can be stale, and being stale is the
       *ordinary* way to arrive here rather than an error: disconnecting a
       source is a thing this section does, and the tab that was open on it
       does not close itself. So the page names the id it was given — the
       operator is going to compare it with whatever they pasted — says what
       usually happened, and hands back the list. */
    return (
      <FormPage title="Source" crumbs={crumbs}>
        <div className={styles.state} data-test="source-not-found">
          <p className={styles.stateTitle}>No connection with that id</p>
          <p className={styles.stateBody}>
            Nothing on this platform is connected under this id. A source that
            was disconnected — here or in another tab — is the ordinary way to
            arrive at this address, and the connections list is where the ones
            that still exist are.
          </p>
          <p className={styles.stateId}>{sourceId}</p>
          <span>
            <Tooltip content="Back to sources">
              <Link
                to="/sources"
                search={{}}
                data-test="source-not-found-back"
                aria-label="Back to sources"
                className={buttonClass({ size: "icon-sm" })}
              >
                <ArrowLeft aria-hidden="true" />
              </Link>
            </Tooltip>
          </span>
        </div>
      </FormPage>
    )
  }

  /* Every act on this page asks *this connection's* project, never the shift:
     `sources.edit` is a project permission, and the person who administers the
     project one source feeds may only be watching the next one along. The
     sentence names the key, because that is the word the operator calls the
     project by and the word the list is already showing them. */
  const where = projectOf(session, connection.projectId)?.key
  const editDenial = can(session, "sources.edit", connection.projectId)
    ? null
    : needsLabel("sources.edit", where)

  /* The one non-permission denial on this page, and it is deliberate: native
     intake has no remote end to disconnect from, so the act is refused by the
     product rather than by a role. `denied` and not `disabled` for the same
     reason a denial uses it — a disabled control fires no pointer events, so
     the sentence explaining the refusal would exist and be unreachable. */
  const disconnectDenial = !connection.removable
    ? NATIVE_DISCONNECT_REFUSAL
    : editDenial

  const testing = testConnection.isPending
  const admitted = admittedCount(connection, tickets)
  const projectKey = where ?? connection.projectId
  const native = connection.kind === "native"

  const dropProbe = () =>
    setProbe((current) => (current === null ? current : null))

  const onTest = () => {
    if (editDenial) {
      return
    }
    testConnection.mutate(connection.id, { onSuccess: setProbe })
  }

  const onSaveWatch = (patch: {
    enabled: boolean
    filter: string
    mode: AdmissionMode
  }) => {
    if (!can(session, "sources.edit", connection.projectId)) {
      return
    }
    // The watch form still owns enabled / filter / mode. The host's
    // admission-rule id and the connection's project id are the only
    // facts the dashboard adds at save time.
    const ruleId = admissionRules.data?.find((rule) => rule.enabled === patch.enabled)?.id ?? null
    saveWatch.mutate(
      {
        connectionId: connection.id,
        projectId: connection.projectId,
        ruleId,
        ...patch,
      },
      {
        onSuccess: () =>
          toast.success("Watch saved", { description: connection.name }),
      }
    )
  }

  const onSaveConnection = (patch: {
    auth: SourceAuth
    account: string
    baseUrl: string
    secretEnvRef: string
  }) => {
    if (!can(session, "sources.edit", connection.projectId)) {
      return
    }
    updateConnection.mutate(
      { connectionId: connection.id, ...patch },
      {
        onSuccess: () =>
          toast.success("Connection saved", { description: connection.name }),
      }
    )
  }

  const onDisconnect = () => {
    if (!connection.removable || editDenial) {
      setDisconnecting(false)
      return
    }
    disconnect.mutate(connection.id, {
      onSuccess: () => {
        toast.success("Source disconnected", { description: connection.name })
        // The record this page is about is gone, so the page is about nothing.
        // `guard.leave` because this departure is the point rather than an
        // accident — the same free pass a cancel and a successful save get.
        guard.leave(() => {
          void navigate({ to: "/sources", search: {}, replace: true })
        })
      },
    })
    setDisconnecting(false)
  }

  const failure =
    disconnect.error ?? saveWatch.error ?? updateConnection.error ?? null

  return (
    <FormPage
      title={connection.name}
      crumbs={crumbs}
      summary={`${SOURCE_KIND_LABEL[connection.kind]} · ${projectKey} · ${connectionNote(connection, tickets)}`}
      actions={
        <>
          <ConnectionStateBadge state={connection.state} />

          <Tooltip content={editDenial ?? "Test connection"}>
            <Button
              size="icon-sm"
              variant="outline"
              data-test="source-test"
              denied={editDenial}
              disabled={testing}
              aria-busy={testing || undefined}
              aria-label={`Test the connection to ${connection.name}`}
              onClick={onTest}
            >
              {testing ? (
                <Loader2 className={tableStyles.spin} aria-hidden="true" />
              ) : (
                <PlugZap aria-hidden="true" />
              )}
            </Button>
          </Tooltip>

          <Tooltip content={disconnectDenial ?? "Disconnect"}>
            <Button
              size="icon-sm"
              variant="destructive"
              data-test="source-disconnect"
              denied={disconnectDenial}
              disabled={disconnect.isPending}
              aria-label={`Disconnect ${connection.name}`}
              onClick={() => setDisconnecting(true)}
            >
              <Unplug aria-hidden="true" />
            </Button>
          </Tooltip>
        </>
      }
    >
      {failure ? (
        <Notice tone="bad" data-test="source-failure">
          {failure.message} Nothing moved — this page is back as it was.
        </Notice>
      ) : null}

      {connection.state === "error" ? (
        // The badge in the header says *that* it is broken; this is the only
        // place on the page that says *why*, in the provider's own words. A
        // bare code would send the operator to the provider to find out what a
        // line here could have told them.
        <Notice tone="bad" data-test="source-error">
          {connection.reason ??
            "the provider refused, and said nothing useful."}
        </Notice>
      ) : null}

      <Section
        variant="region"
        id="source-facts"
        title="what this connection is"
        data-test="source-facts"
      >
        <dl className={styles.facts}>
          <dt className={styles.factName}>provider</dt>
          <dd className={styles.factValue}>
            <span className={styles.factBrand}>
              {/* The mark where the provider has one that survives being
                  drained to the chrome's own colour, and the word where it does
                  not — the fallback lives in the component, exactly as it does
                  in the list's provider column. */}
              <BrandTag
                brand={SOURCE_KIND_BRAND[connection.kind]}
                label={SOURCE_KIND_LABEL[connection.kind]}
              />
            </span>
          </dd>

          <dt className={styles.factName}>auth</dt>
          <dd className={styles.factValue}>{AUTH_LABEL[connection.auth]}</dd>

          <dt className={styles.factName}>instance</dt>
          <dd className={styles.factValue}>
            {connection.baseUrl ?? connectionHost(connection)}
          </dd>

          <dt className={styles.factName}>account</dt>
          <dd className={styles.factValue}>{connection.account}</dd>

          <dt className={styles.factName}>credential</dt>
          <dd className={styles.factValue}>
            {connection.secretEnvRef
              ? /* code-shaped name the host resolves — the dashboard never
                 * shows the value, only the name, which is the structural
                 * "no secret leaves the host" answer. */
              <>
                <code data-test="source-secret-env">{connection.secretEnvRef}</code>
                <span className={styles.factNote}>
                  resolved on the host at probe / webhook time. The
                  dashboard never sees the value — replacing it means
                  changing the env var on the host and patching the
                  connection&apos;s <code>secretEnvRef</code>.
                </span>
              </>
              : connection.secretStoredAt ?? "none"}
            {!connection.secretEnvRef && connection.secretStoredAt ? (
              <span className={styles.factNote}>
                {/* Mock mode (legacy): the seed stamps a date and the
                 * product will never say anything about the secret itself. */}
                stored write-only, and never shown again — not on this page,
                not in a form, not through the api. Replacing it means
                connecting again.
              </span>
            ) : null}
            {!connection.secretEnvRef && !connection.secretStoredAt ? (
              <span className={styles.factNote}>
                native intake has no remote end, so there is nothing to
                authenticate against.
              </span>
            ) : null}
          </dd>

          <dt className={styles.factName}>last sync</dt>
          <dd className={styles.factValue}>
            {native ? "—" : (connection.lastSyncAt ?? "never")}
          </dd>
        </dl>
      </Section>

      {connection.watch ? (
        <Section
          variant="region"
          id="source-watch"
          title="watch and filter"
          data-test="source-watch"
        >
          <WatchForm
            connection={connection}
            // Real mode: derive the watch the form edits from the host's
            // admission rule for this project. The dashboard joins the
            // sibling collection on the client side because the host models
            // rules as a flat list, not a nested field on the connection
            // (issue #40). Mock mode keeps the seed's nested `watch`.
            watch={
              admissionRules.data && admissionRules.data.length > 0
                ? admissionRuleToWatch(admissionRules.data[0])
                : connection.watch
            }
            busy={saveWatch.isPending}
            onSave={onSaveWatch}
            onDirtyChange={setWatchDirty}
          />
        </Section>
      ) : (
        <Section
          variant="region"
          id="source-watch"
          title="watch and filter"
          data-test="source-no-watch"
        >
          {/* `null` rather than a watch that is off, which is the honest shape:
              there is no remote system to watch, so there is no filter, no
              admission mode and nothing to map a status back onto. Native
              tickets arrive because a person wrote one. */}
          <Notice data-test="native-no-watch">
            Native intake has no watch. Tickets arrive here because somebody
            filed one, not because a filter admitted it — so there is nothing to
            turn on and nothing to narrow.
          </Notice>

          {/* The write-back preview still stands, and it is the one place that
              says the other half out loud: there is nowhere to write a status
              back *to*. A section that simply omitted it would leave the
              operator to infer that native is missing a feature the four
              providers have, when what is true is that native is the tracker. */}
          <StatusMappingPreview kind={connection.kind} mapping={[]} />
        </Section>
      )}

      {native ? null : (
        <Section
          variant="region"
          id="source-connection"
          title="the connection"
          data-test="source-connection"
        >
          <ConnectionForm
            connection={connection}
            probe={probe}
            probing={testing}
            busy={updateConnection.isPending}
            onDraftChange={dropProbe}
            onSave={onSaveConnection}
            onDirtyChange={setConnectionDirty}
          />
        </Section>
      )}

      <Section
        variant="region"
        id="source-handoffs"
        title="where this goes"
        data-test="source-handoffs"
      >
        <div className={styles.handoffs}>
          <div className={styles.handoff}>
            <span className={styles.handoffCount}>{admitted}</span>
            <span className={styles.handoffText}>
              {native
                ? "tickets filed here. They are in the catalog with everything else — "
                : "tickets admitted from here. They are in the catalog with everything else — "}
              {/* Deliberately not `/tasks?q=<name>`. The catalog's search reads
                  a ticket's title, id and app, and a connection's name is none
                  of those — a link that carried one would land the operator on
                  an empty screen, which is worse than not narrowing at all.
                  Said out loud rather than implied. */}
              the catalog is not narrowed to this source.
            </span>
            <Link
              to="/tasks"
              search={{}}
              className={styles.handoffLink}
              data-test="handoff-tasks"
            >
              open the catalog
            </Link>
          </div>

          <div className={styles.handoff}>
            <span className={styles.handoffCount}>{projectKey}</span>
            <span className={styles.handoffText}>
              the project this connection feeds. Editing a source is granted per
              project, so this is what every act on this page answers to.
            </span>
            <Link
              to="/projects/$projectId"
              params={{ projectId: connection.projectId }}
              className={styles.handoffLink}
              data-test="handoff-project"
            >
              open the project
            </Link>
          </div>

          {native ? (
            <div className={styles.handoff}>
              <span className={styles.handoffCount}>+</span>
              <span className={styles.handoffText}>
                write a ticket straight into this project&apos;s intake. It is a
                member&apos;s act rather than an administrator&apos;s, so it has
                its own screen and its own permission.
              </span>
              <Link
                to="/sources/$sourceId/ticket/new"
                params={{ sourceId: connection.id }}
                className={styles.handoffLink}
                data-test="handoff-new-ticket"
              >
                file a ticket
              </Link>
            </div>
          ) : null}
        </div>
      </Section>

      <ConfirmDialog
        open={disconnecting}
        danger
        title="Disconnect this source?"
        /* The project is named for the same reason the row names it: the list
           this page is reached from mixes them, and cutting a credential is the
           last moment to notice it is the wrong project's. */
        body={`${connection.name} · ${projectKey} — the credential is dropped and nothing more is admitted from here. Tickets already taken keep their runs; reconnecting means a new secret.`}
        confirmLabel="Disconnect"
        cancelLabel="Keep it"
        onConfirm={onDisconnect}
        onCancel={() => setDisconnecting(false)}
      />

      <ConfirmDialog
        open={guard.asking}
        title="Leave without saving?"
        body="The watch and connection details you changed on this page are not saved yet. Leaving drops them and the source stays exactly as it was."
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}
