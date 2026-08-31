import { X } from "lucide-react"

import type { GrantRow } from "@/domains/identity/model/types"
import { cn } from "@/shared/lib/utils"
import { ROLES, can, needsLabel, type Session } from "@/shared/session"
import { Button, Tooltip, rankSort, type DataColumn } from "@/shared/ui"

import styles from "./identity-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getGrantId = (grant: GrantRow) => grant.id

/**
 * Roles sort by standing, not by spelling — `viewer` before `platform-admin`
 * because that is what the list means, not because `v` precedes `p`. The order
 * is `ROLES` itself, so the column and the grant form cannot disagree about
 * what the six are or which way they run.
 */
const roleSort = rankSort(
  Object.fromEntries(ROLES.map((role, index) => [role, index]))
)

export interface GrantColumnsOptions {
  session: Session
  /** Scopes present in the list — the `scope` filter's options. */
  scopes: string[]
  revokingId: string | null
  onRevoke: (grant: GrantRow) => void
}

/**
 * Subject, role, scope — the three columns the whole authorisation model is.
 *
 * There is no fourth. A role is not a thing that can be edited here or
 * anywhere: the six live in code and the database holds only the fact that
 * somebody was given one. That is why this list has a revoke and no edit — a
 * grant with a different role in it is a different grant.
 */
export function createGrantColumns({
  session,
  scopes,
  revokingId,
  onRevoke,
}: GrantColumnsOptions): DataColumn<GrantRow>[] {
  const denial = can(session, "identity.manage")
    ? null
    : needsLabel("identity.manage")

  return [
    {
      accessorKey: "subjectLabel",
      header: "subject",
      cell: ({ row }) => (
        <span
          className={cn(
            styles.value,
            row.original.subjectInactive && styles.inert
          )}
          title={row.original.subjectLabel}
        >
          {row.original.subjectLabel}
        </span>
      ),
      meta: {
        width: 200,
        pinned: true,
        label: "subject",
        filter: {
          kind: "text",
          placeholder: "filter subject, name, role…",
          match: (grant, needle) =>
            `${grant.subjectLabel} ${grant.subjectName} ${grant.role} ${grant.scopeLabel}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "subjectKind",
      header: "kind",
      cell: ({ row }) => (
        <span className={styles.scope}>
          {row.original.subjectKind === "api-key" ? "api key" : "user"}
        </span>
      ),
      meta: {
        width: 96,
        filter: {
          kind: "select",
          placeholder: "users and keys",
          options: [
            { value: "user", label: "user" },
            { value: "api-key", label: "api key" },
          ],
        },
      },
    },
    {
      accessorKey: "subjectName",
      header: "name",
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.subjectName}>
          {row.original.subjectName}
        </span>
      ),
      meta: { width: 160 },
    },
    {
      accessorKey: "role",
      header: "role",
      sortFn: roleSort,
      cell: ({ row }) => (
        <span className={styles.role}>{row.original.role}</span>
      ),
      meta: {
        width: 132,
        filter: {
          kind: "select",
          placeholder: "all roles",
          // The six, from the same constant the grant form reads. There is no
          // seventh anywhere in this product, including in a filter.
          options: ROLES.map((role) => ({ value: role, label: role })),
        },
      },
    },
    {
      accessorKey: "scopeLabel",
      header: "scope",
      cell: ({ row }) => (
        <span className={styles.scope}>{row.original.scopeLabel}</span>
      ),
      meta: {
        width: 132,
        label: "scope",
        filter: {
          kind: "select",
          placeholder: "all scopes",
          options: scopes.map((scope) => ({ value: scope, label: scope })),
        },
      },
    },
    {
      accessorKey: "grantedAt",
      header: "granted",
      cell: ({ row }) => (
        <span className={styles.scope}>{row.original.grantedAt}</span>
      ),
      meta: { width: 112 },
    },
    {
      id: "actions",
      header: "actions",
      enableSorting: false,
      cell: ({ row }) => {
        const grant = row.original
        const busy = revokingId === grant.id
        return (
          <span className={styles.actions}>
            <Tooltip content={denial ?? "Revoke grant"}>
              <Button
                size="icon-sm"
                variant="destructive"
                data-test="grant-revoke"
                denied={denial}
                disabled={busy}
                aria-busy={busy || undefined}
                aria-label={`Revoke ${grant.role} on ${grant.scopeLabel} from ${grant.subjectLabel}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onRevoke(grant)
                }}
              >
                <X aria-hidden="true" />
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 72, align: "end", label: "actions" },
    },
  ]
}
