import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  escalatedRunsToApprovals,
  toApproval,
  type EscalatedRunsPageWire,
} from "@/domains/approvals/api/mappers"
import type {
  Approval,
  ApprovalDecision,
} from "@/domains/approvals/model/types"
import { runsQueryKey } from "@/domains/runs/api/queries"
import { getApiV1Runs } from "@/shared/api/_generated/clients/getApiV1Runs"
import { postApiV1RunsRunidApprove } from "@/shared/api/_generated/clients/postApiV1RunsRunidApprove"
import { postApiV1RunsRunidCancel } from "@/shared/api/_generated/clients/postApiV1RunsRunidCancel"
import { APPROVALS_SEED } from "@/shared/api/mock"
import { env } from "@/shared/config/env"

export const approvalsQueryKey = ["approvals"] as const

let mockQueue: Approval[] | null = null

function ensureQueue(): Approval[] {
  if (!mockQueue) {
    mockQueue = APPROVALS_SEED.map(toApproval)
  }
  return mockQueue
}

/* Real mode has no approvals endpoint, because it needs none: a decision
   waiting on a human *is* a run sitting in Escalated, and the host's own
   filter DSL names that set exactly (`status==Escalated` — the value parses
   case-insensitively against the stored enum name). The queue is the runs
   list narrowed to it, newest activity first, which is also the order an
   operator works the queue in. */
const ESCALATED_FILTER = "status==Escalated"
const ESCALATED_SORT = "UpdatedAt,desc"

async function listApprovals(): Promise<Approval[]> {
  if (env.useMock) {
    return [...ensureQueue()]
  }
  const page = await getApiV1Runs({
    filter: ESCALATED_FILTER,
    sort: ESCALATED_SORT,
    page: 1,
    pageSize: 100,
  })
  return escalatedRunsToApprovals(page as EscalatedRunsPageWire)
}

async function decideApproval(
  id: string,
  decision: ApprovalDecision
): Promise<Approval[]> {
  if (env.useMock) {
    if (decision === "review") {
      return [...ensureQueue()]
    }
    mockQueue = ensureQueue().filter((item) => item.id !== id)
    return [...mockQueue]
  }

  // Real mode reuses the runs mutations the duty screen already speaks:
  // approve walks the run out of Escalated, cancel tears it down. Review
  // only ever opened the details — there is nothing to ask the host for.
  if (decision === "approve") {
    await postApiV1RunsRunidApprove(id)
  } else if (decision === "reject") {
    await postApiV1RunsRunidCancel(id, { reason: null })
  }
  const refreshed = await listApprovals()
  return refreshed
}

export function useApprovalsQuery() {
  return useQuery({
    queryKey: approvalsQueryKey,
    queryFn: listApprovals,
  })
}

export function useApprovalDecisionMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      id,
      decision,
    }: {
      id: string
      decision: ApprovalDecision
    }) => decideApproval(id, decision),
    onSuccess: (next) => {
      queryClient.setQueryData(approvalsQueryKey, next)
    },
    onSettled: async () => {
      // A decision the host took moves the run on the duty list as well;
      // leaving that cache alone would show the queue empty and the run
      // still escalated until its own poll came round.
      await queryClient.invalidateQueries({ queryKey: runsQueryKey })
    },
  })
}
