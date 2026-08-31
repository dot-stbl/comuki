import { CheckCheck, RotateCw } from "lucide-react"
import { toast } from "sonner"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useApprovalDecisionMutation,
  useApprovalsQuery,
} from "@/domains/approvals/api/queries"
import type { ApprovalDecision } from "@/domains/approvals/model/types"
import { ApprovalCard } from "@/domains/approvals/ui/approval-card"
import { can, useSession } from "@/shared/session"
import { Button, Tooltip } from "@/shared/ui"

import styles from "./approvals-page.module.css"

const SKELETON_COUNT = 3

export function ApprovalsPage() {
  const { data = [], isLoading, isError, error, refetch } = useApprovalsQuery()
  const decision = useApprovalDecisionMutation()
  const session = useSession()

  const onAction = (id: string, action: ApprovalDecision) => {
    // The cards already refuse the click; this is the same rule stated where
    // the write happens, so a future caller cannot reach the queue by
    // rendering its own button. Asked against the item's own project, because
    // the queue mixes them and the right is held per project.
    const item = data.find((entry) => entry.id === id)
    if (!item || !can(session, "plans.approve", item.projectId)) {
      return
    }
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

  const ready = !isLoading && !isError

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[
            { label: "observe", to: "/runs" },
            { label: "approvals" },
          ]}
          title="Approvals"
          summary={
            ready ? (
              <>
                <span className={styles.strong}>{data.length}</span> awaiting
                decision
              </>
            ) : undefined
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="approvals-loading">
            {Array.from({ length: SKELETON_COUNT }).map((_, index) => (
              <span key={index} className={styles.skeletonCard} />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>Failed to load approvals</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="approvals-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {ready && data.length === 0 ? (
          <div className={styles.empty} data-test="approvals-empty">
            <CheckCheck className={styles.emptyIcon} aria-hidden="true" />
            <span>
              <p className={styles.emptyTitle}>Queue empty</p>
              <p className={styles.emptyBody}>Nothing awaiting a human.</p>
            </span>
          </div>
        ) : null}

        {ready && data.length > 0 ? (
          <div className={styles.queue}>
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
