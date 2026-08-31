import { useMemo, useState } from "react"
import { Link, useNavigate } from "@tanstack/react-router"
import { Plus } from "lucide-react"

import type { UserRow } from "@/domains/identity/model/types"
import { useUserDisabledAct } from "@/domains/identity/ui/use-user-disabled"
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
import { createUserColumns, getUserId } from "./users-columns"

export interface UsersPanelProps {
  users: UserRow[]
  /** An address to narrow to on arrival — see `IdentityPage`'s `focus`. */
  initialFilter?: string
}

/**
 * Who exists, and whether they can get in.
 *
 * The panel holds its own filters and its own acts rather than taking them
 * from the page, because the three lists on this screen are narrowed and used
 * independently: "which accounts are disabled" and "who holds approver on
 * atlas" are two questions, and one filter across both would make the screen a
 * single table with a gap in the middle.
 *
 * Two of the three acts left this file when the forms became pages. What is
 * still here is the one act that is genuinely a question rather than an edit:
 * disabling an account is a sentence, and answering it on a page the operator
 * had to travel to would lose their place in a list they are working down.
 */
export function UsersPanel({ users, initialFilter }: UsersPanelProps) {
  const session = useSession()
  const navigate = useNavigate()
  const manage = useCan("identity.manage")
  // The sentence and the asymmetry — enabling asks nothing, disabling asks —
  // are shared with the person's own page rather than written twice. See
  // `use-user-disabled.ts`.
  const disable = useUserDisabledAct()

  const [filters, setFilters] = useState<DataTableFilterValues>(() => {
    const seeded: DataTableFilterValues = {}
    if (initialFilter) {
      seeded.email = initialFilter
    }
    return seeded
  })
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  // Named locally because the column memo keys on it: a mutation running
  // against one row must rebuild the columns, and nothing else must.
  const busyId = disable.busyId

  // Linking a subject is an edit, so it has a screen and an address. The row's
  // control stays a `Button` rather than becoming a link: it is gated, and
  // `denied` is a button's property — an anchor has no way to refuse and
  // explain itself, which is the whole rule.
  const onLink = (user: UserRow) => {
    void navigate({
      to: "/identity/users/$userId/link",
      params: { userId: user.id },
    })
  }

  const columns = useMemo(
    () =>
      createUserColumns({
        session,
        busyId,
        onLink,
        onToggleDisabled: disable.toggle,
      }),
    // `disable.toggle` and `onLink` close over a mutation and a navigate that
    // are stable across renders, so listing them would rebuild every column on
    // every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [session, busyId]
  )

  const rows = useMemo(
    () => applyDataFilters(users, filters, columns),
    [users, filters, columns]
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
            // Gated rather than hidden: the act stays where it was and says
            // what it needs. A shorter screen teaches nobody what to ask for.
            // Allowed, it is navigation and spelled as navigation.
            //
            // Two words, so the glyph carries the act and the tooltip carries
            // the words. The `aria-label` keeps them either way — a tooltip
            // describes, it never becomes the name.
            manage.allowed ? (
              <Tooltip content="New user">
                <Link
                  to="/identity/users/new"
                  data-test="user-new"
                  aria-label="New user"
                  className={buttonClass({ size: "icon-sm" })}
                >
                  <Plus aria-hidden="true" />
                </Link>
              </Tooltip>
            ) : (
              <Tooltip content={manage.denial ?? "New user"}>
                <Button
                  size="icon-sm"
                  data-test="user-new"
                  denied={manage.denial}
                  aria-label="New user"
                >
                  <Plus aria-hidden="true" />
                </Button>
              </Tooltip>
            )
          }
          trailing={
            <span className={tableStyles.count} data-test="users-count">
              {rows.length} shown
            </span>
          }
        />
      </div>
      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getUserId}
          density="compact"
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          sorting={sorting}
          onSortingChange={setSorting}
          columnSizing={columnSizing}
          onColumnSizingChange={setColumnSizing}
          emptyLabel={
            hasActiveFilters(filters)
              ? "no accounts match the current filters"
              : "nobody exists on this platform yet"
          }
        />
      </div>

      {/* A confirmation is not an edit, so it stayed a dialog while the forms
          became pages. The words are the act's own — this screen only says
          where they appear. */}
      <ConfirmDialog {...disable.dialog} />
    </div>
  )
}
