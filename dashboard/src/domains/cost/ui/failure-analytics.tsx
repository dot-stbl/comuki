import { X } from "lucide-react"

import type { CostFailure } from "@/domains/cost/model/types"
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card"

export interface FailureAnalyticsProps {
  rows: CostFailure[]
}

export function FailureAnalytics({ rows }: FailureAnalyticsProps) {
  return (
    <Card size="sm" className="gap-0 py-0">
      <CardHeader className="flex flex-row items-center justify-between border-b px-4 py-3">
        <CardTitle className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
          FailureAnalytics
        </CardTitle>
        <span className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
          where it breaks
        </span>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 px-4 py-4">
        {rows.map((row) => (
          <div key={row.stage} className="flex items-start gap-2">
            <span className="mt-0.5 inline-flex size-5 items-center justify-center rounded-full bg-st-failed/15 text-st-failed">
              <X className="size-3" />
            </span>
            <div className="flex min-w-0 flex-1 flex-col gap-0.5">
              <div className="flex items-center gap-2">
                <span className="w-20 font-mono text-xs text-foreground">
                  {row.stage}
                </span>
                <span className="font-mono text-xs tabular-nums text-st-failed">
                  {Math.round(row.rate * 100)}%
                </span>
              </div>
              <span className="truncate text-xs text-muted-foreground">
                {row.note}
              </span>
            </div>
          </div>
        ))}
      </CardContent>
    </Card>
  )
}
