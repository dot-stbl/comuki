import { useMemo, useState } from "react"
import { getRouteApi, Link } from "@tanstack/react-router"
import {
  ChevronLeft,
  Cpu,
  DollarSign,
  GitBranch,
  Timer,
} from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { getStageInspector, useRunQuery } from "@/domains/runs/api/queries"
import {
  formatCost,
  formatDuration,
  formatTokens,
} from "@/domains/runs/model/format"
import { StageInspectorPanel } from "@/domains/runs/ui/stage-inspector"
import { StagePipeline } from "@/domains/runs/ui/stage-pipeline"
import { Button } from "@/shared/ui/button"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/shared/ui/empty"
import { RunIdChip } from "@/shared/ui/run-id-chip"
import { Skeleton } from "@/shared/ui/skeleton"
import { StatusBadge } from "@/shared/ui/status-badge"

const runDetailRoute = getRouteApi("/runs/$runId")

export function RunDetailPage() {
  const { runId } = runDetailRoute.useParams()
  const { data, isLoading, isError, error } = useRunQuery(runId)
  const [selected, setSelected] = useState<string | null>(null)

  const selectedKey = selected ?? data?.current ?? data?.stages[0]?.key ?? ""
  const selectedStage =
    data?.stages.find((stage) => stage.key === selectedKey) ?? data?.stages[0]
  const inspector = useMemo(() => {
    if (!data || !selectedKey) {
      return null
    }
    return getStageInspector(data.id, selectedKey)
  }, [data, selectedKey])

  const briefHtml = useMemo(() => {
    if (!data) {
      return ""
    }
    return data.brief.replace(/`([^`]+)`/g, "<code>$1</code>")
  }, [data])

  return (
    <AppShell>
      <div className="flex flex-col gap-4">
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="flex flex-col gap-2">
            <nav className="flex items-center gap-1 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
              <Button variant="ghost" size="icon-xs" asChild>
                <Link to="/runs">
                  <ChevronLeft />
                </Link>
              </Button>
              <Link to="/runs" className="hover:text-foreground">
                runs
              </Link>
              <span>/</span>
              <span>{data?.app ?? "…"}</span>
              <span>/</span>
              <span className="text-foreground">run_{runId}</span>
            </nav>
            {isLoading ? (
              <Skeleton className="h-7 w-96" />
            ) : (
              <h1 className="max-w-3xl text-lg font-semibold tracking-tight">
                {data?.title ?? `Run ${runId}`}
              </h1>
            )}
          </div>
          {data ? <StatusBadge status={data.status} /> : null}
        </header>

        {isLoading ? (
          <div className="flex flex-col gap-3">
            <Skeleton className="h-20 w-full" />
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-64 w-full" />
          </div>
        ) : null}

        {isError ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyTitle>Run unavailable</EmptyTitle>
              <EmptyDescription>
                {error instanceof Error ? error.message : "Unknown error"}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {data && selectedStage && inspector ? (
          <>
            <div className="flex flex-col gap-3 rounded-lg border border-border bg-card p-3">
              <div className="flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
                <RunIdChip id={data.id} />
                <span className="inline-flex items-center gap-1 font-mono">
                  <Cpu className="size-3" />
                  {data.model}
                </span>
                <span className="inline-flex items-center gap-1 font-mono">
                  <Timer className="size-3" />
                  {formatDuration(data.durationSec)}
                </span>
                <span className="inline-flex items-center gap-1 font-mono">
                  <DollarSign className="size-3" />
                  {formatCost(data.cost)} · {formatTokens(data.tokens)} tok
                </span>
                <span className="inline-flex items-center gap-1 font-mono">
                  <GitBranch className="size-3" />
                  {data.revision.rules} · {data.revision.sdk}
                </span>
              </div>
              <p
                className="text-sm leading-relaxed text-foreground [&_code]:rounded [&_code]:bg-muted [&_code]:px-1 [&_code]:font-mono [&_code]:text-xs"
                dangerouslySetInnerHTML={{ __html: briefHtml }}
              />
              <StagePipeline
                stages={data.stages}
                current={data.current}
                selected={selectedKey}
                onSelect={setSelected}
              />
            </div>

            <StageInspectorPanel
              stage={selectedStage}
              index={data.stages.indexOf(selectedStage) + 1}
              total={data.stages.length}
              info={inspector}
            />
          </>
        ) : null}
      </div>
    </AppShell>
  )
}
