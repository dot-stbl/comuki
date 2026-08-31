import { Loader2, Ban } from "lucide-react"

import {
  budgetShare,
  endpointOf,
  expiryReading,
  isLive,
  keyState,
  scopeReading,
} from "@/domains/models/model/keys"
import type { ModelEndpoint, VirtualKey } from "@/domains/models/model/types"
import { can, needsLabel, projectOf, type Session } from "@/shared/session"
import {
  Button,
  Tooltip,
  keySort,
  rankSort,
  type DataColumn,
} from "@/shared/ui"

import { BudgetMeter } from "./budget-meter"
import { KeyStateBadge } from "./model-badges"
import styles from "./models-table.module.css"

export interface KeyColumnsOptions {
  endpoints: ModelEndpoint[]
  /** Whether the caps in this table are actually being applied right now. */
  enforced: boolean
  revokingId: string | null
  onRevoke: (entry: VirtualKey) => void
  /**
   * The shift itself, not an answer about it.
   *
   * `models.manage` is a *platform* permission: it reads platform roles alone,
   * so no `projectId` is passed with it even for a key scoped to one project.
   * Who may revoke is a fact about the platform; what the key can reach is a
   * fact about the key. It arrives as the session rather than as a computed
   * check because TanStack calls `cell` as a plain function while it builds a
   * row — a `useCan` in there is a hook called outside a render, which
   * typechecks and then throws.
   */
  session: Session
}

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getKeyId = (entry: VirtualKey) => entry.id

/** Expired first — the keys that stopped working while nobody was told. */
const stateSort = rankSort({ expired: 0, live: 1, revoked: 2 })

/** Fullest cap first, so the one about to stop paying is at the top. */
const budgetSort = keySort((value) => -Number(value ?? 0))

/**
 * Virtual keys — the product's own idea, and the table has to show why it is
 * one.
 *
 * Everything that constrains a key is *inside* it: the route it may take, the
 * models it may name, the scope it holds in, the cap it may spend and the date
 * it stops. A leaked key therefore buys one endpoint, a listed handful of
 * models, whatever is left of a budget, until a day the holder does not
 * control — and every one of those five is a column here, in that order, so the
 * row reads as the argument rather than as an inventory.
 *
 * The models a key may reach are listed rather than counted. `3 models` is a
 * number somebody then has to go and expand; the list is the security reading.
 *
 * The secret itself is never a column. It is shown once at creation and never
 * again — the prefix is the handle that stands in for it everywhere afterwards.
 */
export function createKeyColumns({
  endpoints,
  enforced,
  revokingId,
  onRevoke,
  session,
}: KeyColumnsOptions): DataColumn<VirtualKey>[] {
  const projectKey = (projectId: string) =>
    projectOf(session, projectId)?.key ?? projectId

  return [
    {
      accessorKey: "prefix",
      header: "key",
      cell: ({ row }) => (
        <span className={styles.strong} title={row.original.label}>
          {row.original.prefix}
        </span>
      ),
      meta: {
        width: 116,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: "filter key, label, model…",
          match: (entry, needle) =>
            `${entry.prefix} ${entry.label} ${entry.models.join(" ")}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "label",
      header: "what for",
      cell: ({ row }) => (
        <span className={styles.note} title={row.original.label}>
          {row.original.label}
        </span>
      ),
      meta: { width: 176, label: "what for" },
    },
    {
      id: "scope",
      accessorFn: (entry) =>
        entry.scope.kind === "platform" ? "platform" : entry.scope.projectId,
      header: "scope",
      cell: ({ row }) => (
        <span className={styles.value}>
          {scopeReading(row.original, projectKey)}
        </span>
      ),
      meta: {
        width: 100,
        filter: {
          kind: "select",
          placeholder: "all scopes",
          options: [
            { value: "platform", label: "platform" },
            ...session.projects.map((project) => ({
              value: project.id,
              label: project.key,
            })),
          ],
        },
      },
    },
    {
      id: "route",
      accessorFn: (entry) => entry.endpointId,
      header: "route",
      // One key, one upstream. That is the containment: a key cannot be
      // replayed against a different endpoint even if the model names match.
      cell: ({ row }) => {
        const endpoint = endpointOf(endpoints, row.original.endpointId)
        return endpoint ? (
          <span className={styles.value} title={endpoint.baseUrl}>
            {endpoint.name}
          </span>
        ) : (
          <span className={styles.faint}>—</span>
        )
      },
      meta: {
        width: 124,
        filter: {
          kind: "select",
          placeholder: "all routes",
          options: endpoints.map((endpoint) => ({
            value: endpoint.id,
            label: endpoint.name,
          })),
        },
      },
    },
    {
      id: "models",
      accessorFn: (entry) => entry.models.join(" "),
      header: "may reach",
      cell: ({ row }) => (
        <span className={styles.models} title={row.original.models.join(", ")}>
          {row.original.models.map((model, index) => (
            <span key={model} className={styles.model}>
              {index > 0 ? (
                <span className={styles.modelSep} aria-hidden="true">
                  ·{" "}
                </span>
              ) : null}
              {model}
            </span>
          ))}
        </span>
      ),
      meta: { width: 192, label: "may reach" },
    },
    {
      id: "budget",
      accessorFn: budgetShare,
      header: "spent of cap",
      sortFn: budgetSort,
      cell: ({ row }) => (
        <BudgetMeter entry={row.original} enforced={enforced} />
      ),
      meta: { width: 172, label: "spent of cap" },
    },
    {
      accessorKey: "expiresInSec",
      header: "expires",
      // A TTL is only ever read as "is this about to stop", so it is rendered
      // relative and in the past tense once it has lapsed. A lapsed key is a
      // different thing from one with a day left, and they must not look alike.
      cell: ({ row }) => {
        const entry = row.original
        const lapsed = !entry.revoked && entry.expiresInSec <= 0
        return (
          <span
            className={lapsed ? styles.lapsed : styles.value}
            data-test="key-expiry"
            data-lapsed={lapsed ? "" : undefined}
          >
            {expiryReading(entry)}
          </span>
        )
      },
      meta: { width: 112, numeric: true, label: "expires" },
    },
    {
      id: "state",
      accessorFn: keyState,
      header: "state",
      cell: ({ row }) => <KeyStateBadge entry={row.original} />,
      sortFn: stateSort,
      meta: {
        width: 108,
        filter: {
          kind: "select",
          placeholder: "all states",
          options: [
            { value: "live", label: "live" },
            { value: "expired", label: "expired" },
            { value: "revoked", label: "revoked" },
          ],
          match: (entry, value) => keyState(entry) === value,
        },
      },
    },
    {
      id: "actions",
      header: "actions",
      enableSorting: false,
      cell: ({ row }) => {
        const entry = row.original
        // A revoked key cannot be revoked again, and an expired one has already
        // stopped: the act does not exist for those rows, which is not the same
        // as a role being refused it — and only the second one owes a sentence.
        if (!isLive(entry)) {
          return null
        }

        const busy = revokingId === entry.id
        const denial = can(session, "models.manage")
          ? null
          : needsLabel("models.manage")

        // One glyph standing for an irreversible act, so the word it stands in
        // for arrives on hover *and* on focus — which the `title` it replaces
        // never managed. A refused act puts its sentence in the same place,
        // because `denied` keeps the control focusable and hoverable so it can.
        return (
          <span className={styles.actions}>
            <Tooltip content={denial ?? "Revoke this key"}>
              <Button
                size="icon-sm"
                variant="destructive"
                data-test="key-revoke"
                disabled={busy}
                denied={denial}
                aria-busy={busy || undefined}
                aria-label={`Revoke ${entry.prefix}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onRevoke(entry)
                }}
              >
                {busy ? (
                  <Loader2 className={styles.spin} aria-hidden="true" />
                ) : (
                  <Ban aria-hidden="true" />
                )}
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 80, align: "end", label: "actions" },
    },
  ]
}
