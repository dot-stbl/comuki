import { Check, Circle, X } from "lucide-react"

import type { EvalCase, EvalDelta } from "@/domains/knowledge/model/types"
import { Badge } from "@/shared/ui/badge"
import { StatusBadge } from "@/shared/ui/status-badge"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/table"

const DELTA_META: Record<
  EvalDelta,
  { label: string; icon: typeof Check; className: string }
> = {
  "+": {
    label: "improved",
    icon: Check,
    className: "text-st-success border-st-success/40",
  },
  "-": {
    label: "regressed",
    icon: X,
    className: "text-st-failed border-st-failed/40",
  },
  "=": {
    label: "no change",
    icon: Circle,
    className: "text-muted-foreground",
  },
}

export interface EvalHarnessTableProps {
  cases: EvalCase[]
}

export function EvalHarnessTable({ cases }: EvalHarnessTableProps) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>task</TableHead>
          <TableHead>before</TableHead>
          <TableHead>after</TableHead>
          <TableHead className="text-right">delta</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {cases.map((item) => {
          const delta = DELTA_META[item.delta]
          const DeltaIcon = delta.icon
          return (
            <TableRow key={item.task}>
              <TableCell className="font-mono">{item.task}</TableCell>
              <TableCell>
                <StatusBadge
                  status={item.before === "pass" ? "success" : "failed"}
                  size="sm"
                >
                  {item.before}
                </StatusBadge>
              </TableCell>
              <TableCell>
                <StatusBadge
                  status={item.after === "pass" ? "success" : "failed"}
                  size="sm"
                >
                  {item.after}
                </StatusBadge>
              </TableCell>
              <TableCell className="text-right">
                <Badge variant="outline" className={delta.className}>
                  <DeltaIcon />
                  {delta.label}
                </Badge>
              </TableCell>
            </TableRow>
          )
        })}
      </TableBody>
    </Table>
  )
}
