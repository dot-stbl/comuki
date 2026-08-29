import { useMemo, useState } from "react"
import { LayoutGrid, List, Search } from "lucide-react"
import { Link } from "@tanstack/react-router"

import { AppShell } from "@/app/layout/app-shell"
import { useRunsQuery } from "@/domains/runs/api/queries"
import {
  countActive,
  filterRuns,
  uniqueApps,
} from "@/domains/runs/model/filter-runs"
import { formatCost, formatDuration } from "@/domains/runs/model/format"
import type { RunStatusFilter } from "@/domains/runs/model/types"
import { RunCard } from "@/domains/runs/ui/run-card"
import { RunsFilterBar } from "@/domains/runs/ui/runs-filter-bar"
import { Button } from "@/shared/ui/button"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/shared/ui/empty"
import { Skeleton } from "@/shared/ui/skeleton"
import { StatusBadge } from "@/shared/ui/status-badge"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/table"

type ViewMode = "grid" | "table"

export function RunsPage() {
  const { data = [], isLoading, isError, error } = useRunsQuery()
  const [query, setQuery] = useState("")
  const [app, setApp] = useState("all")
  const [status, setStatus] = useState<RunStatusFilter>("all")
  const [view, setView] = useState<ViewMode>("grid")

  const apps = useMemo(() => uniqueApps(data), [data])
  const shown = useMemo(
    () => filterRuns(data, { query, app, status }),
    [data, query, app, status]
  )
  const active = countActive(shown)

  return (
    <AppShell>
      <div className="flex flex-col gap-4">
        <header className="flex flex-wrap items-end justify-between gap-3">
          <div className="flex flex-col gap-1">
            <div className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
              observe / live runs
            </div>
            <h1 className="text-lg font-semibold tracking-tight">Live runs</h1>
            <p className="font-mono text-xs text-muted-foreground">
              {active} active · {shown.length} total
            </p>
          </div>
          <div className="inline-flex rounded-md border border-border p-0.5">
            <Button
              type="button"
              size="icon-sm"
              variant={view === "grid" ? "secondary" : "ghost"}
              aria-pressed={view === "grid"}
              onClick={() => setView("grid")}
            >
              <LayoutGrid />
            </Button>
            <Button
              type="button"
              size="icon-sm"
              variant={view === "table" ? "secondary" : "ghost"}
              aria-pressed={view === "table"}
              onClick={() => setView("table")}
            >
              <List />
            </Button>
          </div>
        </header>

        <RunsFilterBar
          query={query}
          app={app}
          status={status}
          apps={apps}
          total={shown.length}
          onQueryChange={setQuery}
          onAppChange={setApp}
          onStatusChange={setStatus}
        />

        {isLoading ? (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 6 }).map((_, index) => (
              <Skeleton key={index} className="h-36 rounded-lg" />
            ))}
          </div>
        ) : null}

        {isError ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyTitle>Failed to load runs</EmptyTitle>
              <EmptyDescription>
                {error instanceof Error ? error.message : "Unknown error"}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {!isLoading && !isError && shown.length === 0 ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <Search />
              </EmptyMedia>
              <EmptyTitle>No matches</EmptyTitle>
              <EmptyDescription>Adjust search or filters.</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {!isLoading && !isError && shown.length > 0 && view === "grid" ? (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {shown.map((run) => (
              <RunCard key={run.id} run={run} />
            ))}
          </div>
        ) : null}

        {!isLoading && !isError && shown.length > 0 && view === "table" ? (
          <div className="rounded-lg border border-border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>run</TableHead>
                  <TableHead>app</TableHead>
                  <TableHead>stage</TableHead>
                  <TableHead>status</TableHead>
                  <TableHead>time</TableHead>
                  <TableHead>cost</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {shown.map((run) => {
                  const stage =
                    run.stages.find((item) => item.key === run.current)?.label ??
                    run.current
                  return (
                    <TableRow key={run.id} className="cursor-pointer">
                      <TableCell className="font-mono">
                        <Link
                          to="/runs/$runId"
                          params={{ runId: run.id }}
                          className="hover:underline"
                        >
                          {run.id}
                        </Link>
                      </TableCell>
                      <TableCell>{run.app}</TableCell>
                      <TableCell>{stage}</TableCell>
                      <TableCell>
                        <StatusBadge status={run.status} size="sm" />
                      </TableCell>
                      <TableCell className="font-mono tabular-nums">
                        {formatDuration(run.durationSec)}
                      </TableCell>
                      <TableCell className="font-mono tabular-nums">
                        {formatCost(run.cost)}
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </div>
        ) : null}
      </div>
    </AppShell>
  )
}
