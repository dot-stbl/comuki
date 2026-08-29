import { useState } from "react"
import { Pause, Play } from "lucide-react"

import { useSwarmQuery } from "@/domains/swarm/api/queries"
import { SWARM_ROWS } from "@/domains/swarm/model/types"
import { cn } from "@/shared/lib/utils"
import { Button } from "@/shared/ui/button"
import { Skeleton } from "@/shared/ui/skeleton"
import { StatusBadge } from "@/shared/ui/status-badge"

export function SwarmMeter() {
  const { data, isLoading } = useSwarmQuery()
  const [paused, setPaused] = useState(false)

  const total = data
    ? data.running +
      data.waiting +
      data.failed +
      data.queued +
      data.escalated
    : 0

  return (
    <div className="mt-auto flex flex-col gap-2 rounded-md border border-border bg-muted/20 p-2">
      <div className="flex items-center gap-2 px-1">
        <span className="font-mono text-[9px] uppercase tracking-[0.14em] text-muted-foreground">
          swarm
        </span>
        <span className="font-mono text-[9px] text-muted-foreground">
          {total} total
        </span>
        <span
          className={cn(
            "ml-auto inline-flex items-center gap-1 font-mono text-[9px] uppercase tracking-wider",
            paused ? "text-st-failed" : "text-primary"
          )}
        >
          <span
            className={cn(
              "size-1.5 rounded-full",
              paused ? "bg-st-failed" : "bg-primary animate-pulse"
            )}
          />
          {paused ? "paused" : "live"}
        </span>
      </div>

      {isLoading || !data ? (
        <div className="flex flex-col gap-1 px-1">
          {Array.from({ length: 5 }).map((_, index) => (
            <Skeleton key={index} className="h-5 w-full" />
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-0.5">
          {SWARM_ROWS.map((row) => (
            <div
              key={row.status}
              className="flex h-6 items-center gap-2 rounded-sm px-1"
            >
              <StatusBadge status={row.status} size="sm">
                {row.label}
              </StatusBadge>
              <span
                className={cn(
                  "ml-auto font-mono text-xs font-semibold tabular-nums",
                  row.status === "failed" && "text-st-failed",
                  row.status === "queued" && "text-muted-foreground"
                )}
              >
                {data[row.status]}
              </span>
            </div>
          ))}
        </div>
      )}

      <Button
        type="button"
        size="sm"
        variant={paused ? "destructive" : "outline"}
        className="w-full uppercase tracking-wide"
        onClick={() => setPaused((value) => !value)}
      >
        {paused ? <Play /> : <Pause />}
        {paused ? "Resume" : "Pause"}
      </Button>
    </div>
  )
}
