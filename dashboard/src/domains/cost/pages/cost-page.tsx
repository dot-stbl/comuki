import { useMemo } from "react"
import { DollarSign, RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useCostQuery, COST_WINDOW_DAYS } from "@/domains/cost/api/queries"
import { budgetHeat, budgetPercent, successPercent } from "@/domains/cost/model/cost"
import { CostStat } from "@/domains/cost/ui/cost-stat"
import { FailureAnalytics } from "@/domains/cost/ui/failure-analytics"
import { ProxyBudgetMeter } from "@/domains/cost/ui/proxy-budget-meter"
import { SpendByApp } from "@/domains/cost/ui/spend-by-app"
import { SpendByDay } from "@/domains/cost/ui/spend-by-day"
import { env } from "@/shared/config/env"
import { projectOf, useSession } from "@/shared/session"
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
  const query = useCostQuery()
  const session = useSession()

  /* Real mode's slices name projects by id; the operator's word for a project
     is its key, and the rollup is the one place the two meet. */
  const data = useMemo(() => {
    const summary = query.data
    if (!summary || env.useMock || summary.windowDays === undefined) {
      return summary
    }
    return {
      ...summary,
      byApp: summary.byApp.map((row) => ({
        ...row,
        app: projectOf(session, row.app)?.key ?? row.app,
      })),
    }
  }, [query.data, session])

  const { isLoading, isError, error, refetch } = query

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[{ label: "observe", to: "/runs" }, { label: "cost" }]}
          title="Cost & failures"
          summary={env.useMock ? "last 24h" : `last ${COST_WINDOW_DAYS} days`}
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
                prefix={data.perSuccess === null ? undefined : "$"}
                value={
                  data.perSuccess === null
                    ? // The rollup counts spend and runs; the price it never
                      // divided is not ours to invent.
                      "—"
                    : data.perSuccess.toFixed(2)
                }
                sub={
                  data.perSuccess === null
                    ? "not reported by the platform costs api"
                    : "key business metric — per successful task, not per call"
                }
              />
              <CostStat
                name="per-day"
                label="Per day"
                prefix="$"
                value={(data.totalDay ?? 0).toFixed(0)}
                sub={
                  data.successRate === null
                    ? `average over the ${data.windowDays ?? data.byDay.length}-day window`
                    : `${successPercent(data)}% of tasks — green gate`
                }
              />
              {/* The only tile with a consequence written beside it, so the
                  only one that carries heat. The other two are facts about a
                  window that has already happened, and a fact gets no hue. */}
              {data.budget ? (
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
              ) : (
                <CostStat
                  name="window"
                  label={`${data.windowDays ?? data.byDay.length}-day window`}
                  prefix="$"
                  value={(data.windowUsd ?? 0).toFixed(2)}
                  sub={`all-time $${(data.allTimeUsd ?? 0).toFixed(2)} · ${data.windowRuns ?? 0} runs across ${data.byApp.length} ${data.byApp.length === 1 ? "project" : "projects"}`}
                />
              )}
            </div>

            {/* The time half of the report. The three tiles above say what the
                day is; this band says whether the day is an improvement — full
                width, because a week of columns asked to share a row with the
                per-app ranking would crush the one comparison it exists for. */}
            <Section
              id="cost-by-day"
              data-test="cost-by-day"
              title="spend by day"
              note={`the last ${data.byDay.length} days`}
            >
              <SpendByDay days={data.byDay} />
            </Section>

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

            {/* Seeded numbers are fictional and stay marked as such. Real
                mode renders the platform rollup — real data carries no
                badge, which is the whole point of having one. */}
            {env.useMock ? (
              <p className={styles.mock} data-test="cost-mock-mark">
                <DollarSign className={styles.mockIcon} aria-hidden="true" />
                mock snapshot · VITE_USE_MOCK
              </p>
            ) : null}
          </>
        ) : null}
      </div>
    </AppShell>
  )
}
