import type { TaskPriority } from "@/domains/tasks/model/types"
import { cn } from "@/shared/lib/utils"

export interface PriorityPillProps {
  priority: TaskPriority
  className?: string
}

export function PriorityPill({ priority, className }: PriorityPillProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider",
        priority === "high" && "bg-st-failed/15 text-st-failed",
        priority === "normal" && "bg-muted text-muted-foreground",
        priority === "low" && "bg-st-queued/15 text-st-queued",
        className
      )}
    >
      {priority}
    </span>
  )
}
