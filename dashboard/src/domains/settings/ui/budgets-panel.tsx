import { useEffect, useState } from "react"
import { zodResolver } from "@hookform/resolvers/zod"
import { Controller, useForm } from "react-hook-form"

import type { SettingsStopKind } from "@/domains/settings/api/queries"
import type { Budgets } from "@/domains/settings/model/types"
import {
  budgetFormSchema,
  type BudgetFormInput,
  type BudgetFormValues,
} from "@/domains/settings/model/budget-form"
import type { PermissionCheck } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  NumberField,
  Section,
  SwitchField,
} from "@/shared/ui"

import { BudgetMeter } from "./budget-meter"
import styles from "./settings-panel.module.css"

export interface BudgetsPanelProps {
  budgets: Budgets
  busy?: boolean
  onSave: (values: BudgetFormValues) => void
  /**
   * Throw or stand down one of the two stops. Applied the moment the control
   * is pressed — the stops are acts beside the reading, not values in the form
   * below, because an emergency brake that waits for a submit arrives late.
   */
  onToggleStop: (kind: SettingsStopKind, on: boolean) => void
  /**
   * May this session turn a live setting. Covers the save and the two stops
   * both: they write the same kind of thing, so one `settings.live` answer
   * names the permission once. The caps fields stay writable for a role that
   * cannot apply one, and every control that refuses says what it needs.
   */
  save: PermissionCheck
}

/**
 * What the swarm is allowed to spend, and the two stops that halt it.
 *
 * The reading comes first, and the two stops stand beside it rather than under
 * it, because they are not settings — they are the brake and the clutch, and
 * both act the instant they are touched. Throwing the kill-switch is the one
 * act on this panel that asks first: it blocks every new claim for every app
 * at once, and a sentence that names what it stops costs less than an
 * accidental press. Standing it down, and pausing the swarm, run directly —
 * un-pausing and un-blocking restore what was there, and a pause loses
 * nothing: workers running finish, no container is torn down, one press
 * undoes it, and the state word sits beside the meter where an accidental
 * flip is legible the moment it happens.
 *
 * Below the reading, the caps are a form — values, one save — with the
 * hierarchy the meter already insists on: the global cap is the parent number
 * the meter measures against, and the per-task and per-app caps refine it
 * under their own rule. Only the submit is gated; a cap you cannot apply is
 * still a cap worth working out before you go and ask for it.
 */
export function BudgetsPanel({
  budgets,
  busy = false,
  onSave,
  onToggleStop,
  save,
}: BudgetsPanelProps) {
  const [askingKill, setAskingKill] = useState(false)

  const form = useForm<BudgetFormInput, unknown, BudgetFormValues>({
    resolver: zodResolver(budgetFormSchema),
    defaultValues: {
      globalUsd: budgets.globalUsd,
      perTaskUsd: budgets.perTaskUsd,
      perAppUsd: budgets.perAppUsd,
    },
  })

  useEffect(() => {
    form.reset({
      globalUsd: budgets.globalUsd,
      perTaskUsd: budgets.perTaskUsd,
      perAppUsd: budgets.perAppUsd,
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
        <div className={styles.reading}>
          <BudgetMeter budgets={budgets} />

          {/* A rule between the reading and the acts: the meter is what
              happened, the switches are what to do about it, and the two are
              different sentences even though they stand on one line. */}
          <div className={styles.stops} data-test="budget-stops">
            <SwitchField
              id="killSwitch"
              label="Kill-switch"
              data-test="budgets-kill-switch"
              checked={budgets.killSwitch}
              onLabel="claims blocked"
              offLabel="claims open"
              onCheckedChange={(next) => {
                // The one act here that asks first. Standing the switch back
                // down restores what was there, so it runs directly.
                if (next) {
                  setAskingKill(true)
                  return
                }
                onToggleStop("killSwitch", false)
              }}
              denied={save.denial}
              disabled={busy}
              hint="Hard-stop all new claims — every app, every task."
            />
            <SwitchField
              id="pauseSwarm"
              label="Pause swarm"
              data-test="budgets-pause-swarm"
              checked={budgets.pauseSwarm}
              onLabel="paused"
              offLabel="running"
              onCheckedChange={(next) => onToggleStop("pauseSwarm", next)}
              denied={save.denial}
              disabled={busy}
              hint="Soft pause — running workers finish; no new containers start."
            />
          </div>
        </div>

        <ConfirmDialog
          open={askingKill}
          danger
          title="Throw the kill-switch?"
          body="New claims stop now — every app, every task — and stay stopped until the switch is turned back off. Workers mid-step finish what they are holding; nothing queued is cancelled."
          confirmLabel="Block new claims"
          cancelLabel="Leave claims open"
          onConfirm={() => {
            setAskingKill(false)
            onToggleStop("killSwitch", true)
          }}
          onCancel={() => setAskingKill(false)}
        />
      </Section>

      <Section
        variant="screen"
        data-test="settings-budgets"
        title="Budget caps"
        note="the global cap the meter reads · per-task and per-app refine it"
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
          {/* The parent number, anchored on its own row: it is the cap the
              meter above measures against, and a reader who has to work that
              out from the order of three identical boxes has been made to
              guess. */}
          <div className={styles.capGlobal} data-test="budgets-cap-global">
            <Controller
              control={form.control}
              name="globalUsd"
              render={({ field, fieldState }) => (
                <NumberField
                  id="globalUsd"
                  label="global cap"
                  unit="USD"
                  step="1"
                  value={String(field.value ?? "")}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  disabled={busy}
                  error={fieldState.error?.message ?? null}
                  hint="The meter above measures spend against this cap."
                />
              )}
            />
          </div>

          {/* The two refinements, grouped under a rule and a label so they
              read as refinements of the parent rather than as two more caps
              of the same rank. A fieldset rather than a div: the group is
              real to a screen reader too — named through `aria-label`,
              because a `<legend>` renders in the fieldset's border slot
              where no flex gap reaches it (the same defect the priority
              picker and `ChoiceField` lost their legends to). */}
          <fieldset
            className={styles.capRefinements}
            aria-label="refinements"
            data-test="budgets-cap-refinements"
          >
            <span className={styles.capRefinementsLabel}>refinements</span>
            <div className={styles.refineFields}>
              <Controller
                control={form.control}
                name="perTaskUsd"
                render={({ field, fieldState }) => (
                  <NumberField
                    id="perTaskUsd"
                    label="per task"
                    unit="USD"
                    step="0.01"
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
                  <NumberField
                    id="perAppUsd"
                    label="per app"
                    unit="USD"
                    step="1"
                    value={String(field.value ?? "")}
                    onValueChange={field.onChange}
                    onBlur={field.onBlur}
                    disabled={busy}
                    error={fieldState.error?.message ?? null}
                  />
                )}
              />
            </div>
          </fieldset>

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
