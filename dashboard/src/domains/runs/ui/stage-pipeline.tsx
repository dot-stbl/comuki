import { stageColumns } from "@/domains/runs/model/stage-columns"
import type { RunStage } from "@/domains/runs/model/types"
import { cn } from "@/shared/lib/utils"
import { StatusBadge } from "@/shared/ui/status-badge"

export interface StagePipelineProps {
  stages: RunStage[]
  current?: string
  selected?: string
  onSelect?: (key: string) => void
  compact?: boolean
}

const statusBg: Record<RunStage["status"], string> = {
  running: "bg-st-running",
  success: "bg-st-success",
  failed: "bg-st-failed",
  waiting: "bg-st-waiting",
  queued: "bg-st-queued",
  escalated: "bg-st-escalated",
}

export function StagePipeline({
  stages,
  current,
  selected,
  onSelect,
  compact = false,
}: StagePipelineProps) {
  const columns = stageColumns(stages)

  if (compact) {
    return (
      <div className="flex h-1.5 w-full gap-0.5 overflow-hidden rounded-sm">
        {columns.map((column, index) =>
          column.parallel ? (
            <div
              key={`par-${index}`}
              className={cn(
                "flex min-w-0 flex-1 flex-col gap-px",
                column.stages.some((stage) => stage.key === current) &&
                  "ring-1 ring-foreground/40"
              )}
            >
              {column.stages.map((stage) => (
                <div
                  key={stage.key}
                  title={`${stage.label} · ${stage.status}`}
                  className={cn(
                    "h-full min-h-[3px] flex-1",
                    statusBg[stage.status],
                    stage.status === "running" && "animate-pulse"
                  )}
                />
              ))}
            </div>
          ) : (
            <div
              key={column.stages[0].key}
              title={`${column.stages[0].label} · ${column.stages[0].status}`}
              className={cn(
                "min-w-0 flex-1",
                statusBg[column.stages[0].status],
                column.stages[0].status === "running" && "animate-pulse",
                column.stages[0].key === current && "ring-1 ring-foreground/40"
              )}
            />
          )
        )}
      </div>
    )
  }

  return (
    <div className="flex flex-wrap items-stretch gap-2">
      {columns.map((column, index) => (
        <div key={`col-${index}`} className="flex items-stretch gap-2">
          {index > 0 ? (
            <div
              className={cn(
                "w-4 self-center border-t border-border",
                columns[index - 1]?.stages.every(
                  (stage) => stage.status === "success"
                ) && "border-st-success"
              )}
            />
          ) : null}
          {column.parallel ? (
            <div className="flex flex-col gap-1 rounded-md border border-dashed border-border p-1">
              <span className="px-1 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                ∥ {column.stages.length}
              </span>
              {column.stages.map((stage) => (
                <StageNode
                  key={stage.key}
                  stage={stage}
                  selected={selected === stage.key}
                  onSelect={onSelect}
                />
              ))}
            </div>
          ) : (
            <StageNode
              stage={column.stages[0]}
              selected={selected === column.stages[0].key}
              onSelect={onSelect}
            />
          )}
        </div>
      ))}
    </div>
  )
}

function StageNode({
  stage,
  selected,
  onSelect,
}: {
  stage: RunStage
  selected: boolean
  onSelect?: (key: string) => void
}) {
  return (
    <button
      type="button"
      onClick={() => onSelect?.(stage.key)}
      className={cn(
        "flex min-w-24 flex-col gap-1 rounded-md border border-border bg-card px-2 py-1.5 text-left transition-colors hover:bg-muted/40",
        selected && "border-primary ring-1 ring-primary/40"
      )}
    >
      <StatusBadge status={stage.status} size="sm" />
      <span className="font-mono text-xs text-foreground">{stage.label}</span>
    </button>
  )
}
