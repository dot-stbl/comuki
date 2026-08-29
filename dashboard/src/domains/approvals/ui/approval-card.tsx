import { useState } from "react"
import {
  Check,
  ChevronDown,
  Eye,
  GitBranch,
  ImageIcon,
  TriangleAlert,
  X,
  Zap,
} from "lucide-react"

import type {
  Approval,
  ApprovalDecision,
} from "@/domains/approvals/model/types"
import { useRunsQuery } from "@/domains/runs/api/queries"
import { StagePipeline } from "@/domains/runs/ui/stage-pipeline"
import { cn } from "@/shared/lib/utils"
import { Button } from "@/shared/ui/button"
import { Card, CardContent } from "@/shared/ui/card"

const TYPE_META = {
  plan: { icon: GitBranch, label: "Plan" },
  deploy: { icon: Zap, label: "Deploy" },
  baseline: { icon: ImageIcon, label: "Baseline" },
} as const

export interface ApprovalCardProps {
  approval: Approval
  onAction: (id: string, decision: ApprovalDecision) => void
  busy?: boolean
}

export function ApprovalCard({
  approval,
  onAction,
  busy = false,
}: ApprovalCardProps) {
  const [open, setOpen] = useState(false)
  const { data: runs = [] } = useRunsQuery()
  const run = runs.find((item) => item.id === approval.runId)
  const meta = TYPE_META[approval.type]
  const TypeIcon = meta.icon

  return (
    <Card size="sm" className="gap-3">
      <CardContent className="flex flex-col gap-3 px-4">
        <div className="flex flex-wrap items-center gap-2">
          <span className="inline-flex items-center gap-1 rounded-md border border-border px-2 py-0.5 font-mono text-xs">
            <TypeIcon className="size-3" />
            {meta.label}
          </span>
          <span className="inline-flex items-center gap-1.5 font-mono text-xs text-muted-foreground">
            <span className="size-1.5 rounded-full bg-primary" />
            {approval.app}
          </span>
          <span
            className={cn(
              "inline-flex items-center gap-1 rounded-full px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider",
              approval.risk === "high" && "bg-st-failed/15 text-st-failed",
              approval.risk === "medium" && "bg-st-waiting/15 text-st-waiting",
              approval.risk === "low" && "bg-st-success/15 text-st-success"
            )}
          >
            <TriangleAlert className="size-3" />
            {approval.risk}
          </span>
          <span className="ml-auto font-mono text-xs text-muted-foreground">
            {approval.age}
          </span>
        </div>

        <p className="text-sm leading-relaxed text-foreground">
          {approval.summary}
        </p>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={() => setOpen((value) => !value)}
          >
            <ChevronDown
              className={cn(
                "transition-transform",
                open && "rotate-180"
              )}
            />
            {open ? "Hide" : "Details"}
          </Button>
          <span className="flex-1" />
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={busy}
            onClick={() => onAction(approval.id, "review")}
          >
            <Eye />
            Review
          </Button>
          <Button
            type="button"
            size="sm"
            variant="destructive"
            disabled={busy}
            onClick={() => onAction(approval.id, "reject")}
          >
            <X />
            Reject
          </Button>
          <Button
            type="button"
            size="sm"
            disabled={busy}
            onClick={() => onAction(approval.id, "approve")}
          >
            <Check />
            Approve
          </Button>
        </div>

        {open ? (
          <div className="flex flex-col gap-3 border-t border-border pt-3">
            {approval.type === "plan" && run ? (
              <div className="overflow-x-auto">
                <div className="mb-1.5 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                  Plan — stage DAG
                </div>
                <StagePipeline stages={run.stages} />
              </div>
            ) : null}
            {approval.type === "baseline" ? (
              <div className="grid gap-2 sm:grid-cols-2">
                {["baseline", "new"].map((label) => (
                  <div
                    key={label}
                    className="flex h-24 flex-col rounded-md border border-dashed border-border"
                  >
                    <div className="border-b border-border px-2 py-1 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                      {label}
                    </div>
                    <div className="flex flex-1 items-center justify-center text-muted-foreground">
                      <ImageIcon className="size-6" />
                    </div>
                  </div>
                ))}
              </div>
            ) : null}
            <div>
              <div className="mb-1.5 font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                Planner assumptions
              </div>
              <ul className="flex flex-col gap-1">
                {approval.assumptions.map((item) => (
                  <li
                    key={item}
                    className="flex items-start gap-2 text-xs text-foreground"
                  >
                    <span className="mt-0.5 text-muted-foreground">→</span>
                    {item}
                  </li>
                ))}
              </ul>
            </div>
          </div>
        ) : null}
      </CardContent>
    </Card>
  )
}
