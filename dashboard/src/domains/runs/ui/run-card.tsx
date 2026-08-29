import { Link } from "@tanstack/react-router"
import { Cpu, DollarSign, Timer } from "lucide-react"

import { formatCost, formatDuration } from "@/domains/runs/model/format"
import type { RunSummary } from "@/domains/runs/model/types"
import { StagePipeline } from "@/domains/runs/ui/stage-pipeline"
import { RunIdChip } from "@/shared/ui/run-id-chip"
import { StatusBadge } from "@/shared/ui/status-badge"

export interface RunCardProps {
  run: RunSummary
}

export function RunCard({ run }: RunCardProps) {
  return (
    <Link
      to="/runs/$runId"
      params={{ runId: run.id }}
      className="group flex flex-col gap-3 rounded-lg border border-border bg-card p-3 text-left transition-colors hover:border-primary/40 hover:bg-muted/20"
    >
      <div className="flex items-center gap-2">
        <span className="inline-flex items-center gap-1.5 font-mono text-xs text-muted-foreground">
          <span className="size-1.5 rounded-full bg-primary" />
          {run.app}
        </span>
        <span className="flex-1" />
        <StatusBadge status={run.status} size="sm" />
      </div>
      <div className="line-clamp-2 text-sm font-medium text-foreground">
        {run.title}
      </div>
      <StagePipeline stages={run.stages} current={run.current} compact />
      <div className="flex items-center gap-2 text-muted-foreground">
        <RunIdChip id={run.id} />
        <span className="inline-flex items-center gap-1 font-mono text-xs">
          <Timer className="size-3" />
          {formatDuration(run.durationSec)}
        </span>
        <span className="inline-flex items-center gap-1 font-mono text-xs">
          <DollarSign className="size-3" />
          {formatCost(run.cost).replace("$", "")}
        </span>
        <span className="flex-1" />
        <span className="inline-flex items-center gap-1 font-mono text-xs">
          <Cpu className="size-3" />
          {run.model}
        </span>
      </div>
    </Link>
  )
}
