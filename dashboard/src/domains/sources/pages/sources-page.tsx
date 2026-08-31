import { useCallback, useMemo, useState } from "react"
import { Link, useNavigate } from "@tanstack/react-router"
import { Plug, RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useDisconnectSource,
  useTestConnection,
} from "@/domains/sources/api/mutations"
import { useSourcesQuery } from "@/domains/sources/api/queries"
import type {
  ProbeResult,
  SourceConnection,
} from "@/domains/sources/model/types"
import { ConnectionsPanel } from "@/domains/sources/ui/connections-panel"
import { createSourceColumns } from "@/domains/sources/ui/sources-columns"
import { cn } from "@/shared/lib/utils"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  Section,
  Tooltip,
  buttonClass,
} from "@/shared/ui"

import styles from "./sources-page.module.css"

const SKELETON_WIDTHS = ["58%", "82%", "44%", "70%", "36%"]

export interface SourcesPageProps {
  /**
   * A string to narrow the list to on arrival — what `?q=` carries.
   *
   * Two things hand one over. The project detail page sends its sources here as
   * `/sources?q=<project slug>`, and the global palette resolves a project
   * handle to the same address; both rely on the connections list's promoted
   * filter matching the project key as well as the name, account and host it
   * advertises. It seeds the toolbar's own filter rather than filtering behind
   * its back, so the operator can see why the list is short and clear it in one
   * click.
   */
  focus?: string
}

/**
 * Where work comes from.
 *
 * One list, and — since the section learned that editing gets a page — no forms
 * at all. The list is every connection on the platform, not this project's:
 * there is no current project in this shell, and the duty engineer watches the
 * whole platform at once, so every list here mixes projects exactly as the duty
 * board does. What is scoped is not the rows but the **acts**: `sources.edit` is
 * a project permission, and it is asked per row against that row's own project.
 * A person who administers one project and only watches another sees exactly
 * that, on one screen, one row above the other.
 *
 * Native intake appears once per project and refuses to be disconnected. That
 * is not a permission and not an unbuilt control: it is the product's own way
 * of accepting a ticket, and a platform with no way to accept one is not a
 * state this product has. The refusal is enforced in `sources.store.ts` as well
 * as explained on the button.
 *
 * ## What used to be here
 *
 * Three dialogs. Connecting a source is `/sources/new`; a connection's watch
 * and its details are `/sources/$sourceId`; filing a native ticket is
 * `/sources/$sourceId/ticket/new`. What stayed on the row is the pair of acts
 * that are genuinely decisions about a row rather than edits of one — test and
 * disconnect — because answering either on a page the operator had to travel to
 * would lose their place in a list they are working down.
 *
 * There is still deliberately **no ticket list here**. Reading tickets back is
 * Inbox's job (§5), and a second catalog on this screen would be a second place
 * for the same rows to disagree. What moves here is the native row's own count.
 */
export function SourcesPage({ focus }: SourcesPageProps) {
  const { data, isLoading, isError, error, refetch } = useSourcesQuery()
  const session = useSession()
  const navigate = useNavigate()

  const [disconnecting, setDisconnecting] = useState<SourceConnection | null>(
    null
  )
  /** The row probe's answer, and which row asked. */
  const [rowProbe, setRowProbe] = useState<{
    name: string
    result: ProbeResult
  } | null>(null)

  const testConnection = useTestConnection()
  const disconnect = useDisconnectSource()

  const connections = useMemo(() => data?.connections ?? [], [data])
  const tickets = useMemo(() => data?.tickets ?? [], [data])

  const admitting = connections.filter(
    (entry) => entry.watch?.enabled && entry.state === "connected"
  ).length
  const broken = connections.filter((entry) => entry.state === "error").length

  // The button already refuses a denied click, but every handler answers the
  // same question again on the way in: the gate is the permission, not the
  // control that happens to be carrying it today. All of them read the row's
  // project, never the shift.
  //
  // Opening a source is navigation, and it stays a gated `Button` that
  // navigates rather than becoming an anchor: `denied` is a button's property
  // and an anchor has no way to refuse a click and explain itself. The name
  // cell beside it is the anchor, for the readers who want a destination.
  const onOpenSource = useCallback(
    (connection: SourceConnection) => {
      if (!can(session, "sources.edit", connection.projectId)) {
        return
      }
      void navigate({
        to: "/sources/$sourceId",
        params: { sourceId: connection.id },
      })
    },
    [session, navigate]
  )

  const onNewTicket = useCallback(
    (connection: SourceConnection) => {
      if (!can(session, "inbox.take", connection.projectId)) {
        return
      }
      void navigate({
        to: "/sources/$sourceId/ticket/new",
        params: { sourceId: connection.id },
      })
    },
    [session, navigate]
  )

  const testMutate = testConnection.mutate
  const onTest = useCallback(
    (connection: SourceConnection) => {
      if (!can(session, "sources.edit", connection.projectId)) {
        return
      }
      testMutate(connection.id, {
        onSuccess: (result) => setRowProbe({ name: connection.name, result }),
      })
    },
    [session, testMutate]
  )

  const onDisconnect = useCallback(
    (connection: SourceConnection) => {
      if (!connection.removable) {
        return
      }
      if (!can(session, "sources.edit", connection.projectId)) {
        return
      }
      setDisconnecting(connection)
    },
    [session]
  )

  const testingId = testConnection.isPending
    ? (testConnection.variables ?? null)
    : null

  const columns = useMemo(
    () =>
      createSourceColumns({
        projects: session.projects,
        tickets,
        testingId,
        onOpenSource,
        onTest,
        onDisconnect,
        onNewTicket,
        session,
      }),
    [session, tickets, testingId, onOpenSource, onTest, onDisconnect, onNewTicket]
  )

  // Asked without a project, which is the right question for a page-level
  // control: "may this person connect a source *somewhere*?" The form on
  // `/sources/new` then asks it again against the project it actually picked.
  const connectDenial = can(session, "sources.edit")
    ? null
    : needsLabel("sources.edit")

  const failure = disconnect.error

  const ready = !isLoading && !isError

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[{ label: "configure" }, { label: "sources" }]}
          title="Sources"
          summary={
            ready ? (
              <>
                <span className={styles.strong}>{connections.length}</span>{" "}
                connections · <span className={styles.strong}>{admitting}</span>{" "}
                admitting work
                {broken > 0 ? (
                  <>
                    {" · "}
                    <span className={styles.warn}>{broken}</span> in error
                  </>
                ) : null}
              </>
            ) : undefined
          }
          actions={
            ready ? (
              // Two elements for one act, and the split is the access rule.
              // Allowed, it is navigation and it is spelled as navigation — a
              // real anchor wearing the button's recipe, so it can be opened in
              // a tab, copied, and read as a destination by anything that
              // traverses links. Denied, it is a control that refuses and says
              // what it needs: a disabled anchor is not a thing, and an anchor
              // has no `denied`.
              //
              // Three words became a glyph. A plug here, an unplugged one on
              // the row that disconnects and a live one on the row that tests —
              // one family, three distinct shapes, no two acts in this section
              // wearing the same mark.
              connectDenial === null ? (
                <Tooltip content="Connect a source">
                  <Link
                    to="/sources/new"
                    data-test="connect-source"
                    aria-label="Connect a source"
                    className={buttonClass({ size: "icon" })}
                  >
                    <Plug aria-hidden="true" />
                  </Link>
                </Tooltip>
              ) : (
                <Tooltip content={connectDenial}>
                  <Button
                    size="icon"
                    data-test="connect-source"
                    denied={connectDenial}
                    aria-label="Connect a source"
                  >
                    <Plug aria-hidden="true" />
                  </Button>
                </Tooltip>
              )
            ) : null
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="sources-loading">
            {SKELETON_WIDTHS.map((width, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ width }}
              />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>Couldn&apos;t load sources</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="sources-retry"
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
        ) : null}

        {failure ? (
          <p
            className={styles.failure}
            role="alert"
            data-test="sources-failure"
          >
            {failure instanceof Error ? failure.message : "The change failed."}{" "}
            Nothing moved — the list is back as it was.
          </p>
        ) : null}

        {rowProbe ? (
          <p
            className={cn(styles.probe, !rowProbe.result.ok && styles.probeBad)}
            role="status"
            data-test="row-probe"
          >
            {rowProbe.name} — {rowProbe.result.message}
          </p>
        ) : null}

        {ready ? (
          <Section
            variant="screen"
            data-test="sources-connections"
            title="Connections"
            note={
              <>
                Every source on the platform, whichever project it feeds. A
                connection carries the credential; its <em>watch</em> carries
                what that credential is allowed to admit — they break
                separately, so they read separately. Editing one is granted per
                project, so a row you administer sits above a row you only watch
                and each says which it is.{" "}
                <span className={styles.code}>native</span> is on every project
                and cannot be disconnected.
              </>
            }
          >
            <ConnectionsPanel
              columns={columns}
              connections={connections}
              initialFilter={focus}
            />
          </Section>
        ) : null}
      </div>

      <ConfirmDialog
        open={disconnecting !== null}
        danger
        title="Disconnect this source?"
        body={
          disconnecting
            ? // The project is named for the same reason the row names it: this
              // list mixes them, and cutting a credential is the last moment to
              // notice it is the wrong project's.
              `${disconnecting.name} · ${projectOf(session, disconnecting.projectId)?.key ?? disconnecting.projectId} — the credential is dropped and nothing more is admitted from here. Tickets already taken keep their runs; reconnecting means a new secret.`
            : ""
        }
        confirmLabel="Disconnect"
        cancelLabel="Keep it"
        onConfirm={() => {
          if (
            disconnecting &&
            disconnecting.removable &&
            can(session, "sources.edit", disconnecting.projectId)
          ) {
            disconnect.mutate(disconnecting.id)
          }
          setDisconnecting(null)
        }}
        onCancel={() => setDisconnecting(null)}
      />
    </AppShell>
  )
}
