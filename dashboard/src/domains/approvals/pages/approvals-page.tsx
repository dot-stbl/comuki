import { RotateCw } from "lucide-react"
import { toast } from "sonner"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useApprovalDecisionMutation,
  useApprovalsQuery,
} from "@/domains/approvals/api/queries"
import { decisionPermissionOf } from "@/domains/approvals/model/decide"
import type { ApprovalDecision } from "@/domains/approvals/model/types"
import { ApprovalCard } from "@/domains/approvals/ui/approval-card"
import { requestFailureMessage } from "@/shared/api/problem"
import { can, useSession } from "@/shared/session"
import { Button, Notice, ScreenState, Tooltip } from "@/shared/ui"

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
    // the queue mixes them and the right is held per project — and by kind:
    // adopting a rule is a different grant than approving a plan.
    const item = data.find((entry) => entry.id === id)
    if (!item || !can(session, decisionPermissionOf(item), item.projectId)) {
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

  /* Which card is being decided, not merely that one is. `busy` on every card
     at once refused approve, reject and review across the whole queue while a
     single decision was in flight — the duty list and the key list both answer
     this by id, and so does this now. */
  const deciding = decision.isPending ? (decision.variables?.id ?? null) : null

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
          <ScreenState
            kind="error"
            title="Failed to load approvals"
            description={requestFailureMessage(error, "Unknown error")}
            action={
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
            }
          />
        ) : null}

        {decision.error ? (
          /* The kit's band, which is what the rest of the product answers a
             failed write with. It used to wear `.stateBody` — the empty
             state's prose class — which is how a banner and a state ended up
             sharing one rule and neither owning it. */
          <Notice tone="bad" data-test="approvals-decision-failed">
            {requestFailureMessage(
              decision.error,
              "The decision did not land."
            )}{" "}
            The queue is as it was — the run is still waiting.
          </Notice>
        ) : null}

        {ready && data.length === 0 ? (
          <ScreenState
            kind="empty"
            title="Queue empty"
            description="Nothing awaiting a human."
            data-test="approvals-empty"
          />
        ) : null}

        {ready && data.length > 0 ? (
          <div className={styles.queue}>
            {data.map((approval) => (
              <ApprovalCard
                key={approval.id}
                approval={approval}
                busy={deciding === approval.id}
                onAction={onAction}
              />
            ))}
          </div>
        ) : null}
      </div>
    </AppShell>
  )
}
