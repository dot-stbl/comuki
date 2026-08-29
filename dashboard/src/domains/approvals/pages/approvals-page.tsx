import { CheckCheck } from "lucide-react"
import { toast } from "sonner"

import { AppShell } from "@/app/layout/app-shell"
import {
  useApprovalDecisionMutation,
  useApprovalsQuery,
} from "@/domains/approvals/api/queries"
import type { ApprovalDecision } from "@/domains/approvals/model/types"
import { ApprovalCard } from "@/domains/approvals/ui/approval-card"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/shared/ui/empty"
import { Skeleton } from "@/shared/ui/skeleton"

export function ApprovalsPage() {
  const { data = [], isLoading, isError, error } = useApprovalsQuery()
  const decision = useApprovalDecisionMutation()

  const onAction = (id: string, action: ApprovalDecision) => {
    decision.mutate(
      { id, decision: action },
      {
        onSuccess: () => {
          if (action === "approve") {
            toast.success("Approved", { description: id })
          } else if (action === "reject") {
            toast.message("Rejected", { description: id })
          } else {
            toast.message("Opened review", { description: id })
          }
        },
      }
    )
  }

  return (
    <AppShell>
      <div className="flex flex-col gap-4">
        <header className="flex flex-col gap-1">
          <div className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
            observe / approvals
          </div>
          <h1 className="text-lg font-semibold tracking-tight">Approvals</h1>
          <p className="font-mono text-xs text-muted-foreground">
            {data.length} awaiting decision
          </p>
        </header>

        {isLoading ? (
          <div className="flex flex-col gap-3">
            {Array.from({ length: 3 }).map((_, index) => (
              <Skeleton key={index} className="h-28 rounded-lg" />
            ))}
          </div>
        ) : null}

        {isError ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyTitle>Failed to load approvals</EmptyTitle>
              <EmptyDescription>
                {error instanceof Error ? error.message : "Unknown error"}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {!isLoading && !isError && data.length === 0 ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <CheckCheck />
              </EmptyMedia>
              <EmptyTitle>Queue empty</EmptyTitle>
              <EmptyDescription>Nothing awaiting a human.</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {!isLoading && !isError && data.length > 0 ? (
          <div className="flex flex-col gap-3">
            {data.map((approval) => (
              <ApprovalCard
                key={approval.id}
                approval={approval}
                busy={decision.isPending}
                onAction={onAction}
              />
            ))}
          </div>
        ) : null}
      </div>
    </AppShell>
  )
}
