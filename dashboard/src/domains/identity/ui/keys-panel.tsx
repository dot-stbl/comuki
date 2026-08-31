import { useMemo, useState } from "react"
import { Link } from "@tanstack/react-router"
import { Plus } from "lucide-react"
import { toast } from "sonner"

import { useRevokeApiKeyMutation } from "@/domains/identity/api/queries"
import type { ApiKeyRow } from "@/domains/identity/model/types"
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

import styles from "./identity-panels.module.css"
import tableStyles from "./identity-table.module.css"
import { createApiKeyColumns, getApiKeyId } from "./keys-columns"

export interface KeysPanelProps {
  keys: ApiKeyRow[]
  /** A prefix to narrow to on arrival — see `IdentityPage`'s `focus`. */
  initialFilter?: string
}

/**
 * Keys: what exists, what it opens, and what is still working that shouldn't
 * be.
 *
 * The secret is not here and never was — making a key is a form, so it is a
 * page (`/identity/keys/new`), and the one showing of the plaintext happens
 * there, in state, above that page. This list only ever knows the prefix,
 * which is all the store keeps.
 *
 * Revoking stays a dialog: it is a question with one sentence of consequence,
 * asked in the middle of reading a list, and sending the operator to a screen
 * to answer it would lose the row they were on.
 */
export function KeysPanel({ keys, initialFilter }: KeysPanelProps) {
  const session = useSession()
  const manage = useCan("identity.manage")
  const revokeKey = useRevokeApiKeyMutation()

  const [filters, setFilters] = useState<DataTableFilterValues>(() => {
    const seeded: DataTableFilterValues = {}
    if (initialFilter) {
      seeded.prefix = initialFilter
    }
    return seeded
  })
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const [revoking, setRevoking] = useState<ApiKeyRow | null>(null)

  const revokingId = revokeKey.isPending ? (revokeKey.variables ?? null) : null

  const columns = useMemo(
    () => createApiKeyColumns({ session, revokingId, onRevoke: setRevoking }),
    [session, revokingId]
  )

  const rows = useMemo(
    () => applyDataFilters(keys, filters, columns),
    [keys, filters, columns]
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
            // Two words, so the glyph carries the act and the tooltip carries
            // the words. The `aria-label` keeps them either way — a tooltip
            // describes, it never becomes the name.
            manage.allowed ? (
              <Tooltip content="New key">
                <Link
                  to="/identity/keys/new"
                  data-test="key-new"
                  aria-label="New key"
                  className={buttonClass({ size: "icon-sm" })}
                >
                  <Plus aria-hidden="true" />
                </Link>
              </Tooltip>
            ) : (
              <Tooltip content={manage.denial ?? "New key"}>
                <Button
                  size="icon-sm"
                  data-test="key-new"
                  denied={manage.denial}
                  aria-label="New key"
                >
                  <Plus aria-hidden="true" />
                </Button>
              </Tooltip>
            )
          }
          trailing={
            <span className={tableStyles.count} data-test="keys-count">
              {rows.length} shown
            </span>
          }
        />
      </div>
      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getApiKeyId}
          density="compact"
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          sorting={sorting}
          onSortingChange={setSorting}
          columnSizing={columnSizing}
          onColumnSizingChange={setColumnSizing}
          emptyLabel={
            hasActiveFilters(filters)
              ? "no keys match the current filters"
              : "no keys have been made yet"
          }
        />
      </div>

      <ConfirmDialog
        open={revoking !== null}
        danger
        title="Revoke this key?"
        body={
          revoking
            ? `${revoking.prefix} stops working immediately and its grants go with it. Anything still presenting it starts failing to authenticate. This cannot be undone — a replacement is a new key with a new secret.`
            : ""
        }
        confirmLabel="Revoke key"
        cancelLabel="Cancel"
        onCancel={() => setRevoking(null)}
        onConfirm={() => {
          const key = revoking
          setRevoking(null)
          if (!key) {
            return
          }
          revokeKey.mutate(key.id, {
            onSuccess: () => {
              toast.message("Key revoked", { description: key.prefix })
            },
          })
        }}
      />
    </div>
  )
}
