import type { ReactNode } from "react"

import { Card, CardContent } from "@/shared/ui/card"

export interface StatCardProps {
  label: string
  value: ReactNode
  sub: string
  children?: ReactNode
}

export function StatCard({ label, value, sub, children }: StatCardProps) {
  return (
    <Card size="sm">
      <CardContent className="flex flex-col gap-2 px-4">
        <div className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
          {label}
        </div>
        <div className="text-2xl font-semibold tracking-tight tabular-nums">
          {value}
        </div>
        {children}
        <p className="text-xs text-muted-foreground">{sub}</p>
      </CardContent>
    </Card>
  )
}
