import { useMemo, useState } from "react"
import { Link } from "@tanstack/react-router"
import { Plus } from "lucide-react"
import { toast } from "sonner"

import { useRevokeRoleMutation } from "@/domains/identity/api/queries"
import type { GrantRow } from "@/domains/identity/model/types"
import { useCan, useSession } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  DataTable,
  DataTableToolbar,
  Tooltip,
  applyDataFilters,
  buttonClass,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import { createGrantColumns, getGrantId } from "./grants-columns"
import styles from "./identity-panels.module.css"
import tableStyles from "./identity-table.module.css"

export interface GrantsPanelProps {
  grants: GrantRow[]
  /** A subject to narrow to on arrival — see `IdentityPage`'s `focus`. */
  initialFilter?: string
}

/**
 * Every grant on the platform: subject, role, scope.
 *
 * This is the whole authorisation model rendered as a table, and it is short on
 * purpose — there is no role editor here because there is no role editor
 * anywhere. The list has a grant and a revoke; a grant with a different role in
 * it is a different grant.
 *
 * Writing one is a form, so it is a page: `/identity/grants/new`. Revoking one
 * is a question, so it stays a dialog.
 */
export function GrantsPanel({ grants, initialFilter }: GrantsPanelProps) {
  const session = useSession()
  const manage = useCan("identity.manage")
  const revokeRole = useRevokeRoleMutation()

  const [filters, setFilters] = useState<DataTableFilterValues>(() => {
    const seeded: DataTableFilterValues = {}
    if (initialFilter) {
      seeded.subjectLabel = initialFilter
    }
    return seeded
  })
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const [revoking, setRevoking] = useState<GrantRow | null>(null)

  // The scopes actually present, so the filter offers what the list contains
  // rather than every project that has ever existed.
  const scopes = useMemo(
    () => [...new Set(grants.map((grant) => grant.scopeLabel))].sort(),
    [grants]
  )

  const revokingId = revokeRole.isPending ? (revokeRole.variables ?? null) : null

  const columns = useMemo(
    () =>
      createGrantColumns({
        session,
        scopes,
        revokingId,
        onRevoke: setRevoking,
      }),
    [session, scopes, revokingId]
  )

  const rows = useMemo(
    () => applyDataFilters(grants, filters, columns),
    [grants, filters, columns]
  )

  return (
    <div className={styles.panel}>
      <div className={styles.toolbar}>
        <DataTableToolbar
          columns={columns}
          filters={filters}
          onFiltersChange={setFilters}
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          leading={
            // Three words, so the glyph carries the act and the tooltip
            // carries the words. The `aria-label` keeps them either way — a
            // tooltip describes, it never becomes the name.
            manage.allowed ? (
              <Tooltip content="Grant a role">
                <Link
                  to="/identity/grants/new"
                  data-test="grant-new"
                  aria-label="Grant a role"
                  className={buttonClass({ size: "icon-sm" })}
                >
                  <Plus aria-hidden="true" />
                </Link>
              </Tooltip>
            ) : (
              <Tooltip content={manage.denial ?? "Grant a role"}>
                <Button
                  size="icon-sm"
                  data-test="grant-new"
                  denied={manage.denial}
                  aria-label="Grant a role"
                >
                  <Plus aria-hidden="true" />
                </Button>
              </Tooltip>
            )
          }
          trailing={
            <span className={tableStyles.count} data-test="grants-count">
              {rows.length} shown
            </span>
          }
        />
      </div>
      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getGrantId}
          density="compact"
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          sorting={sorting}
          onSortingChange={setSorting}
          columnSizing={columnSizing}
          onColumnSizingChange={setColumnSizing}
          emptyLabel={
            hasActiveFilters(filters)
              ? "no grants match the current filters"
              : "nobody holds anything yet"
          }
        />
      </div>

      <ConfirmDialog
        open={revoking !== null}
        danger
        title="Revoke this grant?"
        body={
          revoking
            ? `${revoking.subjectLabel} loses ${revoking.role} on ${revoking.scopeLabel}. Anything they were doing with it stops answering.`
            : ""
        }
        confirmLabel="Revoke"
        cancelLabel="Cancel"
        onCancel={() => setRevoking(null)}
        onConfirm={() => {
          const grant = revoking
          setRevoking(null)
          if (!grant) {
            return
          }
          revokeRole.mutate(grant.id, {
            onSuccess: () => {
              toast.message("Grant revoked", {
                description: `${grant.role} on ${grant.scopeLabel}`,
              })
            },
          })
        }}
      />
    </div>
  )
}
