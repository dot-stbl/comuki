import { Loader2, Pencil, PlugZap, Plus, Unplug } from "lucide-react"
import { Link } from "@tanstack/react-router"

import {
  ADMISSION_MODES,
  NATIVE_DISCONNECT_REFUSAL,
  SOURCE_KINDS,
  SOURCE_KIND_BRAND,
  SOURCE_KIND_LABEL,
  admissionLabel,
  admittedCount,
  connectionHost,
  connectionNote,
} from "@/domains/sources/model/providers"
import type {
  NativeTicket,
  SourceConnection,
  SourceState,
} from "@/domains/sources/model/types"
import { ConnectionStateBadge } from "@/domains/sources/ui/connection-state-badge"
import { cn } from "@/shared/lib/utils"
import {
  can,
  needsLabel,
  projectOf,
  type ProjectRef,
  type Session,
} from "@/shared/session"
import {
  BrandTag,
  Button,
  Tooltip,
  rankSort,
  type DataColumn,
} from "@/shared/ui"

import styles from "./sources-table.module.css"

export interface SourceColumnsOptions {
  /** Projects present in the list — the `project` filter's options. */
  projects: ProjectRef[]
  /** Native tickets, for the count on a native row. */
  tickets: NativeTicket[]
  testingId: string | null
  /**
   * Open this connection's own page — where its watch and its details are
   * edited now that both are a screen rather than a modal.
   *
   * Navigation, and it is still a `Button` rather than an anchor because it is
   * *gated*: `denied` is a button's property, and an anchor has no way to
   * refuse a click and explain itself. The identifier cell beside it is the
   * real link, for the readers who want a destination they can copy.
   */
  onOpenSource: (connection: SourceConnection) => void
  onTest: (connection: SourceConnection) => void
  onDisconnect: (connection: SourceConnection) => void
  onNewTicket: (connection: SourceConnection) => void
  /**
   * The signed-in shift itself, not an answer about it.
   *
   * `sources.edit` is a *project* permission and this list mixes projects — a
   * person administers one and only watches the next — so a single yes-or-no
   * computed by the page would be wrong on half the rows.
   *
   * It has to be the session rather than a `useCan` in the cell because a
   * `cell` is not a component: TanStack calls it as a plain function while it
   * builds a row, so a hook inside one is a hook called outside a render, which
   * typechecks and then throws. `can()` and `needsLabel()` are the same rules
   * as plain functions, which is why they are exported beside the hook.
   */
  session: Session
}

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getConnectionId = (connection: SourceConnection) => connection.id

/** Broken first, then off, then working — triage, not the alphabet. */
const STATE_RANK: Record<SourceState, number> = {
  error: 0,
  disabled: 1,
  connected: 2,
}

const stateSort = rankSort(STATE_RANK)

/**
 * The connections list, the address on its identifier cell, and the acts that
 * hang off a row.
 *
 * The source's name is a `<Link>` to that source's own page — the identifier
 * cell and not the whole row, because a row-wide click target would swallow the
 * buttons in the actions column. Two of those buttons are navigation as well
 * and stay `Button`s anyway: they are gated, `denied` is a button's property,
 * and an anchor has no way to refuse a click and explain itself.
 *
 * Filters are declared here, on the column they belong to, so
 * `DataTableToolbar` renders the whole filter bar and `applyDataFilters`
 * evaluates it from this one list.
 *
 * Every act asks the *row's* project, never the shift: `sources.edit` is a
 * project permission, and this list mixes projects by construction because the
 * duty engineer watches the whole platform at once. A denied act keeps its
 * button, its place and its size, and swaps its tooltip for the sentence naming
 * the role that would work and the project it would work on.
 */
export function createSourceColumns({
  projects,
  tickets,
  testingId,
  onOpenSource,
  onTest,
  onDisconnect,
  onNewTicket,
  session,
}: SourceColumnsOptions): DataColumn<SourceConnection>[] {
  return [
    {
      accessorKey: "state",
      header: "state",
      cell: ({ row }) => <ConnectionStateBadge state={row.original.state} />,
      sortFn: stateSort,
      meta: {
        width: 132,
        pinned: true,
        filter: {
          kind: "select",
          placeholder: "all states",
          options: [
            { value: "connected", label: "connected" },
            { value: "error", label: "error" },
            { value: "disabled", label: "disabled" },
          ],
        },
      },
    },
    {
      accessorKey: "name",
      header: "source",
      // The identifier cell is the link, and the row is not. A row-wide click
      // target would swallow the four buttons in the actions column — and an
      // anchor is what makes a source's page a destination somebody can open
      // in a tab, copy into a ticket, or reach with a keyboard the same way
      // they reach a run id two screens over.
      cell: ({ row }) => (
        <Link
          to="/sources/$sourceId"
          params={{ sourceId: row.original.id }}
          className={styles.link}
          data-test="source-link"
          title={row.original.name}
        >
          {row.original.name}
        </Link>
      ),
      meta: {
        width: 184,
        pinned: true,
        filter: {
          kind: "text",
          // The promoted search, and the one field it looks at that the
          // placeholder does not name: the **project key**. The project detail
          // page hands its sources off as `/sources?q=<project slug>`, and a
          // destination that cannot receive what it is sent lands the operator
          // on an empty screen — the contract written out at the top of
          // `app/search/shapes.ts`. It stays out of the placeholder because
          // the key already has its own column and its own select filter, and
          // a search box that advertised every field it quietly also matches
          // would be a paragraph.
          placeholder: "filter source, account, host…",
          match: (connection, needle) => {
            const key = projectOf(session, connection.projectId)?.key ?? ""
            return `${connection.name} ${connection.account} ${connectionHost(connection)} ${connection.reason ?? ""} ${key}`
              .toLowerCase()
              .includes(needle.toLowerCase())
          },
        },
      },
    },
    {
      accessorKey: "kind",
      header: "provider",
      // The mark, where the provider has one that survives being drained to the
      // chrome's own colour, and the word where it does not — the fallback is
      // the component's, not this cell's, so a provider added later cannot end
      // up as an empty cell. The name goes nowhere: it is still what the filter
      // offers, what the row announces and what a hover says.
      cell: ({ row }) => (
        <BrandTag
          brand={SOURCE_KIND_BRAND[row.original.kind]}
          label={SOURCE_KIND_LABEL[row.original.kind]}
        />
      ),
      meta: {
        // Narrower than the word it replaced, and the width the *word* needs is
        // still what sets it — `yandex tracker` is spelled in this column.
        width: 116,
        filter: {
          kind: "select",
          placeholder: "all providers",
          options: SOURCE_KINDS.map((kind) => ({
            value: kind,
            label: SOURCE_KIND_LABEL[kind],
          })),
        },
      },
    },
    {
      // The row's project, shown as the key the operator types and reads. The
      // id is plumbing and the display name is prose; the key is the value.
      // `id` and `accessorKey` both, deliberately: the toolbar and the filter
      // value key read the id (`project`, the word), while the default select
      // predicate reads the field (`projectId`, what the row carries).
      id: "project",
      accessorKey: "projectId",
      header: "project",
      cell: ({ row }) => {
        const project = projectOf(session, row.original.projectId)
        return project ? (
          <span className={styles.muted}>{project.key}</span>
        ) : (
          <span className={styles.muted}>—</span>
        )
      },
      meta: {
        width: 120,
        filter: {
          kind: "select",
          placeholder: "all projects",
          options: projects.map((project) => ({
            value: project.id,
            label: project.key,
          })),
        },
      },
    },
    {
      id: "host",
      accessorFn: connectionHost,
      header: "instance",
      cell: ({ row }) => {
        const host = connectionHost(row.original)
        return (
          <span className={styles.muted} title={row.original.baseUrl ?? host}>
            {host}
          </span>
        )
      },
      meta: { width: 168, label: "instance" },
    },
    {
      accessorKey: "account",
      header: "account",
      cell: ({ row }) => (
        <span className={styles.muted} title={row.original.account}>
          {row.original.account}
        </span>
      ),
      meta: { width: 176 },
    },
    {
      id: "admission",
      accessorFn: admissionLabel,
      header: "admission",
      cell: ({ row }) => (
        <span className={styles.muted}>{admissionLabel(row.original)}</span>
      ),
      meta: {
        width: 128,
        filter: {
          kind: "select",
          placeholder: "all modes",
          options: ADMISSION_MODES.map((mode) => ({
            value: mode.value,
            label: mode.label,
          })),
          // A watch that is off admits nothing, whatever mode it is set to, so
          // it does not answer to a mode filter.
          match: (connection, value) =>
            Boolean(connection.watch?.enabled) &&
            connection.watch?.mode === value,
        },
      },
    },
    {
      id: "admitted",
      accessorFn: (connection) => admittedCount(connection, tickets),
      header: "admitted",
      cell: ({ row }) => admittedCount(row.original, tickets),
      meta: { width: 104, numeric: true, label: "admitted" },
    },
    {
      id: "note",
      accessorFn: (connection) => connectionNote(connection, tickets),
      header: "what is happening",
      cell: ({ row }) => {
        const connection = row.original
        const note = connectionNote(connection, tickets)
        const idle =
          connection.state === "connected" &&
          Boolean(connection.watch?.enabled) &&
          connection.watch?.matched === 0
        return (
          <span
            className={cn(
              styles.note,
              connection.state === "error" && styles.noteBad,
              idle && styles.noteWarn
            )}
            title={note}
            data-test="connection-note"
          >
            {note}
          </span>
        )
      },
      meta: { label: "what is happening" },
    },
    {
      id: "actions",
      header: "actions",
      // Buttons have no order. Say so rather than leaning on the fact that a
      // column without an accessor happens not to sort.
      enableSorting: false,
      cell: ({ row }) => {
        const connection = row.original
        // The only thing a row still does in place is reach the provider, so
        // "busy" and "testing" are the same fact. Saving a watch moved to the
        // source's own page when the dialog did.
        const testing = testingId === connection.id

        // The decision is the row's, so the permission is the row's. The key,
        // not the id and not the display name, is what the sentence names: it
        // is what the operator calls the project, and it is what the `project`
        // column is already showing them.
        const where = projectOf(session, connection.projectId)?.key
        const editDenial = can(session, "sources.edit", connection.projectId)
          ? null
          : needsLabel("sources.edit", where)
        const takeDenial = can(session, "inbox.take", connection.projectId)
          ? null
          : needsLabel("inbox.take", where)

        // The one non-permission use of `denied` in this domain, and it is
        // deliberate: native intake has no remote end to disconnect from, so
        // the act is refused by the product rather than by a role. `disabled`
        // would be wrong for the same reason it is wrong for a denial — a
        // disabled control fires no pointer events, so the sentence explaining
        // the refusal would exist and be unreachable. The rule is enforced in
        // `sources.store.ts` as well; this is the half the operator can read.
        // One spelling, in `providers.ts`, because the source's own page says
        // the same sentence about the same button.
        const disconnectDenial = !connection.removable
          ? NATIVE_DISCONNECT_REFUSAL
          : editDenial

        const native = connection.kind === "native"

        // Four icon-only controls in a 116px cell, so each one owes the reader
        // the word its glyph is standing in for. The kit tooltip carries it on
        // hover *and* on focus, which the `title` attribute it replaces never
        // did — and a denied control puts its sentence there instead, so the
        // explanation arrives in the same place the label would have.
        //
        // `denied` stays `denied`: `Button` keeps an `aria-disabled` control
        // focusable and hoverable precisely so this is reachable. Swapping it
        // for `disabled` would kill the pointer events the tooltip rides on.
        return (
          <span className={styles.actions}>
            {native ? (
              <Tooltip content={takeDenial ?? "New ticket"}>
                <Button
                  size="icon-sm"
                  variant="outline"
                  data-test="source-new-ticket"
                  disabled={testing}
                  denied={takeDenial}
                  aria-label={`New ticket in ${connection.name} on ${where ?? connection.projectId}`}
                  onClick={(event) => {
                    event.stopPropagation()
                    onNewTicket(connection)
                  }}
                >
                  <Plus aria-hidden="true" />
                </Button>
              </Tooltip>
            ) : (
              <>
                <Tooltip content={editDenial ?? "Watch and filter"}>
                  <Button
                    size="icon-sm"
                    variant="outline"
                    data-test="source-edit-watch"
                    disabled={testing}
                    denied={editDenial}
                    aria-label={`Edit the watch on ${connection.name}`}
                    onClick={(event) => {
                      event.stopPropagation()
                      onOpenSource(connection)
                    }}
                  >
                    <Pencil aria-hidden="true" />
                  </Button>
                </Tooltip>
                <Tooltip content={editDenial ?? "Test connection"}>
                  <Button
                    size="icon-sm"
                    variant="outline"
                    data-test="source-test"
                    disabled={testing}
                    denied={editDenial}
                    aria-busy={testing || undefined}
                    aria-label={`Test the connection to ${connection.name}`}
                    onClick={(event) => {
                      event.stopPropagation()
                      onTest(connection)
                    }}
                  >
                    {testing ? (
                      <Loader2 className={styles.spin} aria-hidden="true" />
                    ) : (
                      <PlugZap aria-hidden="true" />
                    )}
                  </Button>
                </Tooltip>
              </>
            )}
            <Tooltip content={disconnectDenial ?? "Disconnect"}>
              <Button
                size="icon-sm"
                variant="destructive"
                data-test="source-disconnect"
                disabled={testing}
                denied={disconnectDenial}
                aria-label={`Disconnect ${connection.name}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onDisconnect(connection)
                }}
              >
                <Unplug aria-hidden="true" />
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 116, align: "end", label: "actions" },
    },
  ]
}
