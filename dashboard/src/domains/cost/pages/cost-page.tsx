import { DollarSign, RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useCostQuery } from "@/domains/cost/api/queries"
import {
  budgetHeat,
  budgetPercent,
  successPercent,
} from "@/domains/cost/model/cost"
import { CostStat } from "@/domains/cost/ui/cost-stat"
import { FailureAnalytics } from "@/domains/cost/ui/failure-analytics"
import { ProxyBudgetMeter } from "@/domains/cost/ui/proxy-budget-meter"
import { SpendByApp } from "@/domains/cost/ui/spend-by-app"
import { Button, Section, Tooltip } from "@/shared/ui"

import styles from "./cost-page.module.css"

const SKELETON_WIDTHS = ["38%", "62%", "50%", "74%"]

/**
 * What a day of the swarm costs, and what it is buying nothing.
 *
 * A report, not a board: opened on a slow clock and read top to bottom, so it
 * scrolls with its content rather than fitting the viewport. Three readings
 * across the top, then the two breakdowns that say where each of them came
 * from — spend by app, and the profiles that fail.
 *
 * There is no chart library under any of this and there does not need to be.
 * Both breakdowns are a shared axis and a handful of lengths, every one of them
 * drawn beside a figure that already states the reading — so a runtime
 * dependency would buy axes, tooltips and a legend for a picture that is
 * complete in words with every bar removed.
 *
 * Nothing on this screen writes. The route gates `cost.view`; there is no act
 * inside to gate.
 */
export function CostPage() {
  const { data, isLoading, isError, error, refetch } = useCostQuery()

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[{ label: "observe", to: "/runs" }, { label: "cost" }]}
          title="Cost & failures"
          summary="last 24h"
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="cost-loading">
            {SKELETON_WIDTHS.map((width, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ width }}
              />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>The report did not load</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="cost-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {data ? (
          <>
            <div className={styles.stats}>
              <CostStat
                name="per-success"
                label="Cost per success"
                prefix="$"
                value={data.perSuccess.toFixed(2)}
                sub="key business metric — per successful task, not per call"
              />
              <CostStat
                name="per-day"
                label="Per day"
                prefix="$"
                value={data.totalDay.toFixed(0)}
                sub={`${successPercent(data)}% of tasks — green gate`}
              />
              {/* The only tile with a consequence written beside it, so the
                  only one that carries heat. The other two are facts about a
                  day that has already happened, and a fact gets no hue. */}
              <CostStat
                name="proxy-budget"
                label="Proxy budget"
                value={String(budgetPercent(data.budget))}
                suffix="%"
                heat={budgetHeat(data.budget)}
                sub={`$${data.budget.used.toFixed(0)} / $${data.budget.cap.toFixed(0)} · kill-switch at cap`}
              >
                <ProxyBudgetMeter budget={data.budget} />
              </CostStat>
            </div>

            <div className={styles.regions}>
              <Section
                id="cost-by-app"
                data-test="cost-by-app"
                title="spend by app"
                note="spend"
              >
                <SpendByApp rows={data.byApp} />
              </Section>

              <Section
                id="cost-failures"
                data-test="cost-failures"
                title="where runs fail"
                note="where it breaks"
              >
                <FailureAnalytics rows={data.failures} />
              </Section>
            </div>

            {/* Seeded numbers are fictional and stay marked as such. */}
            <p className={styles.mock} data-test="cost-mock-mark">
              <DollarSign className={styles.mockIcon} aria-hidden="true" />
              mock snapshot · VITE_USE_MOCK
            </p>
          </>
        ) : null}
      </div>
    </AppShell>
  )
}
