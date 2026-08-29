import { GitBranch, Plus } from "lucide-react"

import type { TaskSource } from "@/domains/tasks/model/types"
import { cn } from "@/shared/lib/utils"

export interface SourceBadgeProps {
  source: TaskSource
  id: string
  className?: string
}

export function SourceBadge({ source, id, className }: SourceBadgeProps) {
  if (source === "manual") {
    return (
      <span
        className={cn(
          "inline-flex items-center gap-1 rounded-md border border-border px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider text-muted-foreground",
          className
        )}
      >
        <Plus className="size-3" />
        manual
      </span>
    )
  }

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-md border border-border px-2 py-0.5 font-mono text-[10px] tracking-wider text-foreground",
        className
      )}
    >
      <GitBranch className="size-3" />
      {id}
    </span>
  )
}
