import { useEffect, useMemo, useState } from "react"
import { zodResolver } from "@hookform/resolvers/zod"
import { Controller, useForm } from "react-hook-form"

import type { ModelRoute } from "@/domains/settings/model/types"
import {
  routingFormSchema,
  type RoutingFormValues,
} from "@/domains/settings/model/routing-form"
import type { PermissionCheck } from "@/shared/session"
import {
  Button,
  DataTable,
  DataTableToolbar,
  Notice,
  Section,
  TextField,
  applyDataFilters,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import { createRoutingColumns, getRouteId } from "./routing-columns"
import styles from "./settings-panel.module.css"
import tableStyles from "./settings-table.module.css"

export interface RoutingPanelProps {
  routes: ModelRoute[]
  busy?: boolean
  onSave: (values: RoutingFormValues) => void
  /** May this session turn a live setting — see `BudgetsPanelProps.save`. */
  save: PermissionCheck
}

/**
 * Which physical model each role is served by.
 *
 * Two bands, and the split is deliberate: the table states what is in force
 * now, and the form under it is the only thing that writes. Three inline edits
 * would each have to ask the same permission question and each be refused
 * separately; one form asks once, and the sentence that refuses it names the
 * whole act rather than a third of it.
 *
 * The escalation policy sits at the bottom as a `Notice` rather than as prose,
 * because it is the rule that explains why the lead model matters at all — the
 * worker is not the model that finishes a hard step, it is the one that tries
 * first.
 */
export function RoutingPanel({
  routes,
  busy = false,
  onSave,
  save,
}: RoutingPanelProps) {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(() => createRoutingColumns(), [])
  const rows = useMemo(
    () => applyDataFilters(routes, filters, columns),
    [routes, filters, columns]
  )

  const lead = routes.find((route) => route.role === "lead")
  const worker = routes.find((route) => route.role === "worker")
  const judge = routes.find((route) => route.role === "judge")

  const form = useForm<RoutingFormValues>({
    resolver: zodResolver(routingFormSchema),
    defaultValues: {
      leadModel: lead?.model ?? "",
      workerModel: worker?.model ?? "",
      judgeModel: judge?.model ?? "",
    },
  })

  useEffect(() => {
    form.reset({
      leadModel: lead?.model ?? "",
      workerModel: worker?.model ?? "",
      judgeModel: judge?.model ?? "",
    })
  }, [form, lead?.model, worker?.model, judge?.model])

  return (
    <div className={styles.stack}>
      <Section
        variant="screen"
        data-test="settings-routing"
        title="Model routing"
        note="role → physical model"
      >
        <div className={styles.toolbar}>
          <DataTableToolbar
            columns={columns}
            filters={filters}
            onFiltersChange={setFilters}
            columnVisibility={columnVisibility}
            onColumnVisibilityChange={setColumnVisibility}
            trailing={
              <span className={tableStyles.count} data-test="routing-count">
                {rows.length} shown
              </span>
            }
          />
        </div>
        <div className={styles.tableArea}>
          <DataTable
            columns={columns}
            data={rows}
            getRowId={getRouteId}
            density="compact"
            columnVisibility={columnVisibility}
            onColumnVisibilityChange={setColumnVisibility}
            sorting={sorting}
            onSortingChange={setSorting}
            columnSizing={columnSizing}
            onColumnSizingChange={setColumnSizing}
            emptyLabel={
              hasActiveFilters(filters)
                ? "no roles match the current filters"
                : "no role is routed"
            }
          />
        </div>
      </Section>

      <Section
        variant="screen"
        data-test="settings-routing-edit"
        title="Edit role → model map"
        note="leading / worker / judge"
      >
        <form
          className={styles.form}
          onSubmit={form.handleSubmit((values) => {
            // Enter inside a text field submits a form without touching the
            // button, so the rule is stated here as well as on the control.
            if (!save.allowed) {
              return
            }
            onSave(values)
          })}
        >
          <div className={styles.fields}>
            <Controller
              control={form.control}
              name="leadModel"
              render={({ field, fieldState }) => (
                <TextField
                  id="leadModel"
                  label="lead"
                  value={field.value}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  disabled={busy}
                  error={fieldState.error?.message ?? null}
                />
              )}
            />
            <Controller
              control={form.control}
              name="workerModel"
              render={({ field, fieldState }) => (
                <TextField
                  id="workerModel"
                  label="worker"
                  value={field.value}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  disabled={busy}
                  error={fieldState.error?.message ?? null}
                />
              )}
            />
            <Controller
              control={form.control}
              name="judgeModel"
              render={({ field, fieldState }) => (
                <TextField
                  id="judgeModel"
                  label="judge"
                  value={field.value}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  disabled={busy}
                  error={fieldState.error?.message ?? null}
                />
              )}
            />
          </div>

          <div className={styles.footer}>
            {/* A button that commits a form keeps its words. `denied` and never
                `disabled` for the refusal: a disabled control fires no pointer
                events, so the sentence naming what is missing never arrives. */}
            <Button
              type="submit"
              size="sm"
              data-test="routing-save"
              denied={save.denial}
              disabled={busy}
              aria-busy={busy || undefined}
            >
              Save routing
            </Button>
          </div>
        </form>
      </Section>

      <Notice data-test="routing-escalation">
        Escalation policy — 2 failed retries on worker escalate to lead. A red
        type gate goes to a debug agent with a pinned revision.
      </Notice>
    </div>
  )
}
