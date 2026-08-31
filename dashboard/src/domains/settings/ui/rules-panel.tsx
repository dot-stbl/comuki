import { useMemo, useState } from "react"

import type { SwarmRule } from "@/domains/settings/model/types"
import {
  DataTable,
  DataTableToolbar,
  Notice,
  Section,
  applyDataFilters,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import { createRuleColumns, getRuleId, uniqueRuleScopes } from "./rules-columns"
import styles from "./settings-panel.module.css"
import tableStyles from "./settings-table.module.css"

export interface RulesPanelProps {
  rules: SwarmRule[]
}

/**
 * The rules every worker is handed with its brief.
 *
 * The conflict reading sits above the table rather than under it, because it is
 * the thing the operator came to check: two rules whose scopes overlap is how a
 * swarm ends up arguing with itself, and finding that out after scrolling five
 * rows is finding it out late. An answer the product just gave, so it is a
 * `Notice` in its `ok` tone — a band that belongs to the section it stands in,
 * not a toast that can be missed.
 *
 * Read-only, and the note says why: the rules live in the client's own
 * repository and change by commit.
 */
export function RulesPanel({ rules }: RulesPanelProps) {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const scopes = useMemo(() => uniqueRuleScopes(rules), [rules])
  const columns = useMemo(() => createRuleColumns(scopes), [scopes])
  const rows = useMemo(
    () => applyDataFilters(rules, filters, columns),
    [rules, filters, columns]
  )

  return (
    <Section
      variant="screen"
      data-test="settings-rules"
      title="Swarm rules"
      note="read-only · rules live in the client's git and change by commit"
    >
      <Notice tone="ok" data-test="rules-conflicts">
        No conflicts found · {rules.length} active rules · scopes don&apos;t
        overlap.
      </Notice>

      <div className={styles.toolbar}>
        <DataTableToolbar
          columns={columns}
          filters={filters}
          onFiltersChange={setFilters}
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          trailing={
            <span className={tableStyles.count} data-test="rules-count">
              {rows.length} shown
            </span>
          }
        />
      </div>
      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getRuleId}
          density="compact"
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          sorting={sorting}
          onSortingChange={setSorting}
          columnSizing={columnSizing}
          onColumnSizingChange={setColumnSizing}
          emptyLabel={
            hasActiveFilters(filters)
              ? "no rules match the current filters"
              : "the rule set is empty"
          }
        />
      </div>
    </Section>
  )
}
