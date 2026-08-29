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
  const label =
    children ?? status.charAt(0).toUpperCase() + status.slice(1)

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
