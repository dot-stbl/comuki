import { useEffect } from "react"
import { zodResolver } from "@hookform/resolvers/zod"
import { Controller, useForm } from "react-hook-form"

import type { Budgets } from "@/domains/settings/model/types"
import {
  budgetFormSchema,
  type BudgetFormInput,
  type BudgetFormValues,
} from "@/domains/settings/model/budget-form"
import type { PermissionCheck } from "@/shared/session"
import { Button, Section, SwitchField, TextField } from "@/shared/ui"

import { BudgetMeter } from "./budget-meter"
import styles from "./settings-panel.module.css"

export interface BudgetsPanelProps {
  budgets: Budgets
  busy?: boolean
  onSave: (values: BudgetFormValues) => void
  /**
   * May this session turn a live setting. Only the save is gated: the fields
   * stay writable so a role that cannot apply a cap can still work out what it
   * would ask for, and the one control that writes says what it needs.
   */
  save: PermissionCheck
}

/**
 * What the swarm is allowed to spend, and the two switches that stop it.
 *
 * The reading comes first and the controls after it, because nobody sets a cap
 * without first looking at what the last one did. The two switches sit below
 * the three caps rather than beside them: a cap is a value and a switch is a
 * sentence, and a sentence in a grid of number boxes reads as a fourth number
 * that has lost its box.
 *
 * Only the submit is gated. The fields stay writable for a role that cannot
 * apply anything, because a cap you cannot set is still a cap worth working out
 * before you go and ask for it — and the control that refuses says what it
 * needs, rather than the form going quietly dead.
 */
export function BudgetsPanel({
  budgets,
  busy = false,
  onSave,
  save,
}: BudgetsPanelProps) {
  const form = useForm<BudgetFormInput, unknown, BudgetFormValues>({
    resolver: zodResolver(budgetFormSchema),
    defaultValues: {
      perTaskUsd: budgets.perTaskUsd,
      perAppUsd: budgets.perAppUsd,
      globalUsd: budgets.globalUsd,
      killSwitch: budgets.killSwitch,
      pauseSwarm: budgets.pauseSwarm,
    },
  })

  useEffect(() => {
    form.reset({
      perTaskUsd: budgets.perTaskUsd,
      perAppUsd: budgets.perAppUsd,
      globalUsd: budgets.globalUsd,
      killSwitch: budgets.killSwitch,
      pauseSwarm: budgets.pauseSwarm,
    })
  }, [budgets, form])

  return (
    <div className={styles.stack}>
      <Section
        variant="screen"
        data-test="settings-budget-reading"
        title="Proxy budget"
        note="what the swarm has spent today against the global cap"
      >
        <BudgetMeter budgets={budgets} />
      </Section>

      <Section
        variant="screen"
        data-test="settings-budgets"
        title="Budget caps"
        note="per-task / per-app / global · kill-switch and pause swarm"
      >
        <form
          className={styles.form}
          onSubmit={form.handleSubmit((values) => {
            // Enter inside a number field submits a form without touching the
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
              name="perTaskUsd"
              render={({ field, fieldState }) => (
                <TextField
                  id="perTaskUsd"
                  label="per task (USD)"
                  type="number"
                  step="0.01"
                  inputMode="decimal"
                  value={String(field.value ?? "")}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  disabled={busy}
                  error={fieldState.error?.message ?? null}
                />
              )}
            />
            <Controller
              control={form.control}
              name="perAppUsd"
              render={({ field, fieldState }) => (
                <TextField
                  id="perAppUsd"
                  label="per app (USD)"
                  type="number"
                  step="1"
                  inputMode="decimal"
                  value={String(field.value ?? "")}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  disabled={busy}
                  error={fieldState.error?.message ?? null}
                />
              )}
            />
            <Controller
              control={form.control}
              name="globalUsd"
              render={({ field, fieldState }) => (
                <TextField
                  id="globalUsd"
                  label="global (USD)"
                  type="number"
                  step="1"
                  inputMode="decimal"
                  value={String(field.value ?? "")}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  disabled={busy}
                  error={fieldState.error?.message ?? null}
                />
              )}
            />
          </div>

          <div className={styles.switches}>
            <Controller
              control={form.control}
              name="killSwitch"
              render={({ field }) => (
                <SwitchField
                  id="killSwitch"
                  label="Kill-switch"
                  data-test="budgets-kill-switch"
                  checked={Boolean(field.value)}
                  onCheckedChange={field.onChange}
                  disabled={busy}
                  hint="Hard-stop all new claims when global cap is hit or toggled on."
                />
              )}
            />
            <Controller
              control={form.control}
              name="pauseSwarm"
              render={({ field }) => (
                <SwitchField
                  id="pauseSwarm"
                  label="Pause swarm"
                  data-test="budgets-pause-swarm"
                  checked={Boolean(field.value)}
                  onCheckedChange={field.onChange}
                  disabled={busy}
                  hint="Soft pause — running workers finish; no new containers start."
                />
              )}
            />
          </div>

          <div className={styles.footer}>
            {/* A button that commits a form keeps its words. `denied` and never
                `disabled` for the refusal: a disabled control fires no pointer
                events, so the sentence naming what is missing never arrives —
                and the control leaves the tab order as well. */}
            <Button
              type="submit"
              size="sm"
              data-test="budgets-save"
              denied={save.denial}
              disabled={busy}
              aria-busy={busy || undefined}
            >
              Save budgets
            </Button>
          </div>
        </form>
      </Section>
    </div>
  )
}
