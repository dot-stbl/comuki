import { DollarSign } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { useCostQuery } from "@/domains/cost/api/queries"
import { FailureAnalytics } from "@/domains/cost/ui/failure-analytics"
import { SpendByApp } from "@/domains/cost/ui/spend-by-app"
import { StatCard } from "@/domains/cost/ui/stat-card"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/shared/ui/empty"
import { Progress } from "@/shared/ui/progress"
import { Skeleton } from "@/shared/ui/skeleton"

export function CostPage() {
  const { data, isLoading, isError, error } = useCostQuery()

  const budgetPct = data
    ? Math.round((data.budget.used / data.budget.cap) * 100)
    : 0

  return (
    <AppShell>
      <div className="flex flex-col gap-4">
        <header className="flex flex-col gap-1">
          <div className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
            observe / cost
          </div>
          <h1 className="text-lg font-semibold tracking-tight">
            Cost &amp; failures
          </h1>
          <p className="font-mono text-xs text-muted-foreground">last 24h</p>
        </header>

        {isLoading ? (
          <div className="grid gap-3 md:grid-cols-3">
            {Array.from({ length: 3 }).map((_, index) => (
              <Skeleton key={index} className="h-28 rounded-lg" />
            ))}
          </div>
        ) : null}

        {isError ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyTitle>Failed to load cost</EmptyTitle>
              <EmptyDescription>
                {error instanceof Error ? error.message : "Unknown error"}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {data ? (
          <>
            <div className="grid gap-3 md:grid-cols-3">
              <StatCard
                label="Cost per success"
                value={
                  <>
                    <span className="mr-0.5 text-base text-muted-foreground">
                      $
                    </span>
                    {data.perSuccess.toFixed(2)}
                  </>
                }
                sub="key business metric — per successful task, not per call"
              />
              <StatCard
                label="Per day"
                value={
                  <>
                    <span className="mr-0.5 text-base text-muted-foreground">
                      $
                    </span>
                    {data.totalDay.toFixed(0)}
                  </>
                }
                sub={`${Math.round(data.successRate * 100)}% of tasks — green gate`}
              />
              <StatCard
                label="Proxy budget"
                value={
                  <>
                    {budgetPct}
                    <span className="ml-0.5 text-base text-muted-foreground">
                      %
                    </span>
                  </>
                }
                sub={`$${data.budget.used.toFixed(0)} / $${data.budget.cap.toFixed(0)} · kill-switch at cap`}
              >
                <Progress value={budgetPct} className="mt-1" />
              </StatCard>
            </div>

            <div className="grid gap-3 lg:grid-cols-2">
              <SpendByApp rows={data.byApp} />
              <FailureAnalytics rows={data.failures} />
            </div>

            <p className="inline-flex items-center gap-1.5 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
              <DollarSign className="size-3" />
              mock snapshot · VITE_USE_MOCK
            </p>
          </>
        ) : null}
      </div>
    </AppShell>
  )
}
