import { RotateCw } from "lucide-react"
import { Trans, useTranslation } from "react-i18next"
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
  const { t } = useTranslation("approvals")
  const { t: tShell } = useTranslation("shell")
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
            toast.success(t("decide.approvedToast"), { description: id })
          } else if (action === "reject") {
            toast.message(t("decide.rejectedToast"), { description: id })
          } else {
            toast.message(t("decide.openedToast"), { description: id })
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
            { label: tShell("crumb.observe"), to: "/runs" },
            { label: tShell("crumb.approvals") },
          ]}
          title={t("registry.title")}
          summary={
            ready ? (
              /* The count stringifies: a Trans slot holding a falsy child (a
                 bare `0`) renders empty, and zero is a reading, not a
                 blank. */
              <Trans
                ns="approvals"
                i18nKey="registry.summary"
                /* The count travels as a value so the plural group resolves
                   (ru agrees with its number), and as the slot so the figure
                   keeps its own styling. It stringifies: a Trans slot holding
                   a falsy child (a bare `0`) renders empty, and zero is a
                   reading, not a blank. */
                values={{ count: data.length }}
                components={{
                  count: (
                    <span className={styles.strong}>{String(data.length)}</span>
                  ),
                }}
              />
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
            title={t("registry.errorTitle")}
            description={requestFailureMessage(error, t("errors.unknown"))}
            action={
              <Tooltip content={t("actions.retry")}>
                <Button
                  size="icon-sm"
                  data-test="approvals-retry"
                  aria-label={t("actions.retry")}
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
            {requestFailureMessage(decision.error, t("decide.refused"))}{" "}
            {t("decide.tail")}
          </Notice>
        ) : null}

        {ready && data.length === 0 ? (
          <ScreenState
            kind="empty"
            title={t("empty.title")}
            description={t("empty.description")}
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
