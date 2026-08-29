import { Activity, Circle, Sparkles } from "lucide-react"

import type { TaskStatus } from "@/domains/tasks/model/types"
import { cn } from "@/shared/lib/utils"

const META: Record<
  TaskStatus,
  {
    icon: typeof Circle
    className: string
  }
> = {
  new: {
    icon: Sparkles,
    className: "bg-st-waiting/15 text-st-waiting",
  },
  queued: {
    icon: Circle,
    className: "bg-st-queued/15 text-st-queued",
  },
  planning: {
    icon: Activity,
    className: "bg-st-running/15 text-st-running",
  },
}

export interface TaskStatusPillProps {
  status: TaskStatus
  className?: string
}

export function TaskStatusPill({ status, className }: TaskStatusPillProps) {
  const meta = META[status]
  const Icon = meta.icon

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider",
        meta.className,
        status === "planning" &&
          "[@media(prefers-reduced-motion:no-preference)]:animate-pulse",
        className
      )}
    >
      <Icon className="size-3" />
      {status}
    </span>
  )
}
