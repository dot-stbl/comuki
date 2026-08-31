import type { ComponentType, ReactNode } from "react"
import {
  Activity,
  Check,
  ChevronUp,
  Circle,
  Clock,
  X,
} from "lucide-react"

import { cn } from "@/shared/lib/utils"

import styles from "./status-badge.module.css"

export type Status =
  | "running"
  | "success"
  | "failed"
  | "waiting"
  | "queued"
  | "escalated"

export interface StatusBadgeProps {
  status: Status
  size?: "sm" | "md"
  className?: string
  children?: ReactNode
}

const statusIcons: Record<Status, ComponentType<{ className?: string }>> = {
  running: Activity,
  success: Check,
  failed: X,
  waiting: Clock,
  queued: Circle,
  escalated: ChevronUp,
}

export function StatusBadge({
  status,
  size = "md",
  className,
  children,
}: StatusBadgeProps) {
  const Icon = statusIcons[status]
  // Verbatim, not title-cased. A status is a value out of a closed vocabulary
  // — the same six strings the filter offers, the API returns and the seed
  // writes — and a value is spelled the way it is stored. Capitalising it here
  // made the badge say 'Running' while the filter beside it said 'running',
  // which reads as two different vocabularies rather than one.
  const label = children ?? status

  return (
    <span
      data-test="status-badge"
      data-status={status}
      className={cn(
        styles.badge,
        styles[status],
        size === "sm" && styles.sm,
        status === "running" && styles.pulse,
        className
      )}
    >
      <Icon className={styles.icon} />
      {label}
    </span>
  )
}
