import { useState } from "react"
import { DollarSign, RotateCw } from "lucide-react"
import { cn } from "@/shared/lib/utils"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useCostQuery } from "@/domains/cost/api/queries"
import { periodDelta } from "@/domains/cost/model/cost"
import { BudgetProgress } from "@/domains/cost/ui/budget-progress"
import { FailureAnalytics } from "@/domains/cost/ui/failure-analytics"
import { ForecastWidget } from "@/domains/cost/ui/forecast-widget"
import { PeriodToggle } from "@/domains/cost/ui/period-toggle"
import { ProxyBudgetMeter } from "@/domains/cost/ui/proxy-budget-meter"
import { SpendByApp } from "@/domains/cost/ui/spend-by-app"
import { SpendByDay } from "@/domains/cost/ui/spend-by-day"
import { SpendByModel } from "@/domains/cost/ui/spend-by-model"
import { TopProjects } from "@/domains/cost/ui/top-projects"
import { TotalSpend } from "@/domains/cost/ui/total-spend"
import { requestFailureMessage } from "@/shared/api/problem"
import { Button, ScreenState, Section, Skeleton, Tooltip } from "@/shared/ui"

import styles from "./cost-page.module.css"

const SKELETON_WIDTHS = ["38%", "62%", "50%", "74%"]

/** The three choices the toggle offers, in the order the report reads them. */
const PERIOD_OPTIONS = [
  { value: "day" as const, label: "today", note: "1d" },
  { value: "week" as const, label: "this week", note: "7d" },
  { value: "month" as const, label: "this month", note: "30d" },
]

const PERIOD_DAYS = {
  day: 1,
  week: 7,
  month: 30,
} as const

/**
 * What the cost page is, in three readings and four breakdowns.
 *
 * A report read top to bottom on a slow clock: three tiles across the
 * top (total spend / forecast / budget progress) and four sections that
 * say where each of them came from (by-day / by-model / top projects /
 * per-app / failures). The period toggle re-shapes the picture —
 * the three tiles track the chosen period; the breakdowns are period-
 * specific by construction.
 *
 * The figures are seeded — `mock snapshot · VITE_USE_MOCK` says so at the
 * bottom — and the story the page tells is the same one the runs screen
 * does: a runaway run on `prometheus`, a cap the budget tile is closing
 * in on, a model mix weighted toward `glm-5.2`. Every breakdown is a
 * shape the operator can read at a glance, none of them a chart library.
 */
export function CostPage() {
  const [period, setPeriod] = useState<"day" | "week" | "month">("day")
  const { data, isLoading, isError, error, refetch } = useCostQuery(period)

  const todayBurn = data?.todaySpend ?? 0
  const todayCap = data?.todayCap ?? 0
  const monthToDate = data?.monthSpend ?? 0
  const monthCap = data?.monthCap ?? 0

  const delta = data
    ? periodDelta(data.totalPeriod, data.totalPreviousPeriod)
    : null
  const burnPerDay =
    data && data.totalPeriod > 0
      ? data.totalPeriod / PERIOD_DAYS[data.period]
      : 0

  const periodLabel =
    period === "day"
      ? "today"
      : `this ${period.replace("week", "week").replace("month", "month")}`

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[{ label: "observe", to: "/runs" }, { label: "cost" }]}
          title="Cost & failures"
          summary={periodLabel}
        />
      }
    >
      <div className={styles.screen}>
        <div className={styles.periodRow} data-test="cost-period-row">
          <PeriodToggle
            value={period}
            onChange={setPeriod}
            options={PERIOD_OPTIONS}
            trailing={
              data && delta !== null ? (
                <span
                  className={cn(
                    styles.deltaChip,
                    delta > 0 && styles.deltaChipUp,
                    delta < 0 && styles.deltaChipDown
                  )}
                  data-test="cost-period-delta"
                >
                  <span className={styles.deltaChipArrow}>
                    {delta > 0 ? "▲" : delta < 0 ? "▼" : "◆"}
                  </span>
                  <span>
                    {Math.abs(delta * 100).toFixed(0)}% vs previous{" "}
                    {periodLabel}
                  </span>
                </span>
              ) : null
            }
          />
        </div>

        {isLoading ? (
          <Skeleton lines={SKELETON_WIDTHS} data-test="cost-loading" />
        ) : null}

        {isError ? (
          <ScreenState
            kind="error"
            title="The report did not load"
            description={requestFailureMessage(error, "Unknown error")}
            action={
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
            }
          />
        ) : null}

        {data ? (
          <>
            <div className={styles.tiles} data-test="cost-tiles">
              <TotalSpend
                total={data.totalPeriod}
                previousTotal={data.totalPreviousPeriod}
                periodLabel={periodLabel}
                burnNote={
                  <>
                    ${burnPerDay.toFixed(2)} / day on average ·{" "}
                    {data.byDay.length} day{data.byDay.length === 1 ? "" : "s"}{" "}
                    observed
                  </>
                }
              />
              <ForecastWidget
                forecast={data.forecast}
                burnRateLabel={`$${data.forecast.burnRatePerDay.toFixed(2)} / day`}
                projectedLabel={`end of ${period.replace("week", "week").replace("month", "month")}`}
                /* The same bar the budget tile draws, from the same
                   component — the forecast is literally spend against a cap,
                   and the two tiles must not be able to disagree about how
                   loud a colour is at the same share. */
                meter={
                  <ProxyBudgetMeter
                    budget={{
                      used: data.forecast.projectedEndOfPeriod,
                      cap: data.forecast.cap,
                    }}
                  />
                }
              />
              <BudgetProgress
                todayBurn={todayBurn}
                todayCap={todayCap}
                monthToDate={monthToDate}
                monthCap={monthCap}
              />
            </div>

            <Section
              id="cost-by-day"
              data-test="cost-by-day"
              title="spend by day"
              note={`the last ${data.byDay.length} day${data.byDay.length === 1 ? "" : "s"}`}
            >
              <SpendByDay days={data.byDay} />
            </Section>

            <div className={styles.regions}>
              <Section
                id="cost-by-model"
                data-test="cost-by-model"
                title="spend by model"
                note="tokens"
              >
                <SpendByModel rows={data.byModel} />
              </Section>

              <Section
                id="top-projects"
                data-test="top-projects-section"
                title="top projects"
                note="this period"
              >
                <TopProjects rows={data.topProjects} />
              </Section>
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
