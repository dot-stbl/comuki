import { useMemo, useState } from "react"
import { Link } from "@tanstack/react-router"
import { Plus } from "lucide-react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { useRevokeApiKeyMutation } from "@/domains/identity/api/queries"
import type { ApiKeyRow } from "@/domains/identity/model/types"
import { requestFailureMessage } from "@/shared/api/problem"
import { useCan, useSession } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  DataTable,
  DataTableToolbar,
  Notice,
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
  const { t } = useTranslation("identity")
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
    () =>
      createApiKeyColumns({
        session,
        revokingId,
        onRevoke: setRevoking,
        t,
      }),
    [session, revokingId, t]
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
              <Tooltip content={t("keysPanel.newKey")}>
                <Link
                  to="/identity/keys/new"
                  data-test="key-new"
                  aria-label={t("keysPanel.newKey")}
                  className={buttonClass({ size: "icon-sm" })}
                >
                  <Plus aria-hidden="true" />
                </Link>
              </Tooltip>
            ) : (
              <Tooltip content={manage.denial ?? t("keysPanel.newKey")}>
                <Button
                  size="icon-sm"
                  data-test="key-new"
                  denied={manage.denial}
                  aria-label={t("keysPanel.newKey")}
                >
                  <Plus aria-hidden="true" />
                </Button>
              </Tooltip>
            )
          }
          trailing={
            <span className={tableStyles.count} data-test="keys-count">
              {t("keysPanel.shown", { count: rows.length })}
            </span>
          }
        />
      </div>

      {/* A revoke that did not land, above the table that still shows the key
          it failed to stop. Saying nothing here is the worst possible answer:
          the operator walks away believing a key is dead. */}
      {revokeKey.error ? (
        <div className={styles.failure}>
          <Notice tone="bad" data-test="key-revoke-failure">
            {requestFailureMessage(
              revokeKey.error,
              t("keysPanel.revokeRefused")
            )}{" "}
            {t("keysPanel.revokeTail")}
          </Notice>
        </div>
      ) : null}

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
              ? t("keysPanel.emptyFiltered")
              : t("keysPanel.emptyNone")
          }
        />
      </div>

      <ConfirmDialog
        open={revoking !== null}
        danger
        title={t("keysPanel.revokeTitle")}
        body={
          revoking ? t("keysPanel.revokeBody", { prefix: revoking.prefix }) : ""
        }
        confirmLabel={t("keysPanel.revokeConfirm")}
        cancelLabel={t("actions.cancel", { ns: "common" })}
        onCancel={() => setRevoking(null)}
        onConfirm={() => {
          const key = revoking
          setRevoking(null)
          if (!key) {
            return
          }
          revokeKey.mutate(key.id, {
            onSuccess: () => {
              toast.message(t("keysPanel.revokedToast"), {
                description: key.prefix,
              })
            },
          })
        }}
      />
    </div>
  )
}
