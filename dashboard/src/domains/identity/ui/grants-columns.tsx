import { X } from "lucide-react"

import {
  sharedIdentityT,
  type IdentityTranslator,
} from "@/domains/identity/model/identity"
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
  /**
   * Copy arrives as a parameter because `cell` is called as a plain function
   * while the table builds a row, so a `useTranslation` inside one throws.
   * The page passes its hook-bound `t`; tests and stories fall back to the
   * shared instance, which answers in the active locale.
   */
  t?: IdentityTranslator
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
  t = sharedIdentityT,
}: GrantColumnsOptions): DataColumn<GrantRow>[] {
  const denial = can(session, "identity.manage")
    ? null
    : needsLabel("identity.manage")

  return [
    {
      accessorKey: "subjectLabel",
      header: t("grantsColumn.subject"),
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
        label: t("grantsColumn.subject"),
        filter: {
          kind: "text",
          placeholder: t("grantsColumn.filterPlaceholder"),
          match: (grant, needle) =>
            `${grant.subjectLabel} ${grant.subjectName} ${grant.role} ${grant.scopeLabel}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "subjectKind",
      header: t("grantsColumn.kind"),
      cell: ({ row }) => (
        <span className={styles.scope}>
          {row.original.subjectKind === "api-key"
            ? t("grantsColumn.apiKey")
            : t("grantsColumn.user")}
        </span>
      ),
      meta: {
        width: 96,
        filter: {
          kind: "select",
          placeholder: t("grantsColumn.usersAndKeys"),
          options: [
            { value: "user", label: t("grantsColumn.user") },
            { value: "api-key", label: t("grantsColumn.apiKey") },
          ],
        },
      },
    },
    {
      accessorKey: "subjectName",
      header: t("grantsColumn.name"),
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.subjectName}>
          {row.original.subjectName}
        </span>
      ),
      meta: { width: 160 },
    },
    {
      accessorKey: "role",
      header: t("grantsColumn.role"),
      sortFn: roleSort,
      cell: ({ row }) => (
        <span className={styles.role}>{row.original.role}</span>
      ),
      meta: {
        width: 132,
        filter: {
          kind: "select",
          placeholder: t("grantsColumn.allRoles"),
          // The six, from the same constant the grant form reads. There is no
          // seventh anywhere in this product, including in a filter.
          options: ROLES.map((role) => ({ value: role, label: role })),
        },
      },
    },
    {
      accessorKey: "scopeLabel",
      header: t("grantsColumn.scope"),
      cell: ({ row }) => (
        <span className={styles.scope}>{row.original.scopeLabel}</span>
      ),
      meta: {
        width: 132,
        label: t("grantsColumn.scope"),
        filter: {
          kind: "select",
          placeholder: t("grantsColumn.allScopes"),
          options: scopes.map((scope) => ({ value: scope, label: scope })),
        },
      },
    },
    {
      accessorKey: "grantedAt",
      header: t("grantsColumn.granted"),
      cell: ({ row }) => (
        <span className={styles.scope}>{row.original.grantedAt}</span>
      ),
      meta: { width: 112 },
    },
    {
      id: "actions",
      header: t("grantsColumn.actions"),
      enableSorting: false,
      cell: ({ row }) => {
        const grant = row.original
        const busy = revokingId === grant.id
        return (
          <span className={styles.actions}>
            <Tooltip content={denial ?? t("grantsColumn.revokeGrant")}>
              <Button
                size="icon-sm"
                variant="destructive"
                data-test="grant-revoke"
                denied={denial}
                loading={busy}
                aria-label={t("grantsColumn.revokeGrantAria", {
                  role: grant.role,
                  scope: grant.scopeLabel,
                  subject: grant.subjectLabel,
                })}
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
      meta: { width: 72, align: "end", label: t("grantsColumn.actions") },
    },
  ]
}
