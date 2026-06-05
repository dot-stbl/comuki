import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"

import { cn } from "@/lib/utils"
import {
  Activity,
  Check,
  ChevronUp,
  Circle,
  Clock,
  X,
} from "lucide-react"

type Status = "running" | "success" | "failed" | "waiting" | "queued" | "escalated"

const statusBadgeVariants = cva(
  "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium",
  {
    variants: {
      status: {
        running: "bg-st-running/15 text-st-running",
        success: "bg-st-success/15 text-st-success",
        failed: "bg-st-failed/15 text-st-failed",
        waiting: "bg-st-waiting/15 text-st-waiting",
        queued: "bg-st-queued/15 text-st-queued",
        escalated: "bg-st-escalated/15 text-st-escalated",
      },
      size: {
        sm: "text-[10px] px-1.5 py-0",
        md: "text-xs px-2 py-0.5",
      },
    },
    defaultVariants: {
      status: "queued",
      size: "md",
    },
  }
)

const statusIcons: Record<Status, React.ComponentType<{ className?: string }>> = {
  running: Activity,
  success: Check,
  failed: X,
  waiting: Clock,
  queued: Circle,
  escalated: ChevronUp,
}

function StatusBadge({
  status,
  size = "md",
  className,
  children,
}: {
  status: Status
  size?: "sm" | "md"
  className?: string
  children?: React.ReactNode
}) {
  const Icon = statusIcons[status]
  const label = children ?? status.charAt(0).toUpperCase() + status.slice(1)

  return (
    <span
      className={cn(
        statusBadgeVariants({ status, size }),
        status === "running" &&
          "[@media(prefers-reduced-motion:no-preference)]:animate-pulse",
        className
      )}
    >
      <Icon className={cn(size === "sm" ? "size-2.5" : "size-3")} />
      {label}
    </span>
  )
}

export { StatusBadge, statusBadgeVariants }
export type { Status }