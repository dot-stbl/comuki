import type { CostByApp } from "@/domains/cost/model/types"
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card"

export interface SpendByAppProps {
  rows: CostByApp[]
}

export function SpendByApp({ rows }: SpendByAppProps) {
  const max = Math.max(...rows.map((row) => row.spend), 1)

  return (
    <Card size="sm" className="gap-0 py-0">
      <CardHeader className="flex flex-row items-center justify-between border-b px-4 py-3">
        <CardTitle className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
          CostBreakdown · by app
        </CardTitle>
        <span className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
          spend
        </span>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 px-4 py-4">
        {rows.map((row) => (
          <div key={row.app} className="grid grid-cols-[7rem_1fr_3.5rem] items-center gap-3">
            <span className="truncate font-mono text-xs text-foreground">
              {row.app}
            </span>
            <div className="h-1.5 overflow-hidden rounded-full bg-muted">
              <div
                className="h-full rounded-full bg-primary"
                style={{ width: `${(row.spend / max) * 100}%` }}
              />
            </div>
            <span className="text-right font-mono text-xs tabular-nums text-muted-foreground">
              ${row.spend.toFixed(1)}
            </span>
          </div>
        ))}
      </CardContent>
    </Card>
  )
}
