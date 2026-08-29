import type { Status } from "@/shared/ui/status-badge"

export type SwarmStatus = Extract<
  Status,
  "running" | "waiting" | "failed" | "queued" | "escalated"
>

export interface SwarmCounts {
  running: number
  waiting: number
  failed: number
  queued: number
  escalated: number
}

export const SWARM_ROWS: Array<{ status: SwarmStatus; label: string }> = [
  { status: "running", label: "running" },
  { status: "waiting", label: "waiting" },
  { status: "escalated", label: "escalated" },
  { status: "failed", label: "failed" },
  { status: "queued", label: "queued" },
]
