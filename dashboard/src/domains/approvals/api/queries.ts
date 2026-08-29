import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { toApproval } from "@/domains/approvals/api/mappers"
import type {
  Approval,
  ApprovalDecision,
} from "@/domains/approvals/model/types"
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

async function listApprovals(): Promise<Approval[]> {
  if (!env.useMock) {
    throw new Error("approvals API not implemented — set VITE_USE_MOCK=true")
  }
  return [...ensureQueue()]
}

async function decideApproval(
  id: string,
  decision: ApprovalDecision
): Promise<Approval[]> {
  if (!env.useMock) {
    throw new Error("approvals API not implemented — set VITE_USE_MOCK=true")
  }
  if (decision === "review") {
    return [...ensureQueue()]
  }
  mockQueue = ensureQueue().filter((item) => item.id !== id)
  return [...mockQueue]
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
  })
}
