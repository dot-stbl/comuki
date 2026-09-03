import { useMutation, useQueryClient } from "@tanstack/react-query"

import { runsQueryKey } from "@/domains/runs/api/queries"
import type { RunSummary } from "@/domains/runs/model/types"
import { approveSeedRun, cancelSeedRun } from "@/shared/api/mock"
import { env } from "@/shared/config/env"

/**
 * Run decisions — the two things the duty screen exists to let a human do.
 *
 * The host's `RunsController` ships only `GET /api/v1/runs` (paged list).
 * `POST /api/v1/runs/{runId}/approve` and `…/{runId}/cancel` are not on
 * the wire today, so the real-mode branch throws rather than pretending
 * to have succeeded — an optimistic write would be undone by the next
 * refetch and the decision would look like a 220ms animation.
 *
 * Mock mode writes to the shared seed store, which the runs query reads;
 * that round-trip is what keeps the UI honest in storybook and dev:mock.
 * When the host grows a decision endpoint, the real-mode branch becomes
 * `postApiV1RunsRunidApprove(runId, …)` / `…/cancel` (kubb-generated);
 * the mock-mode path stays as-is.
 */

async function postDecision(runId: string, decision: "approve" | "cancel") {
  if (!env.useMock) {
    throw new Error(
      `run ${decision} not implemented — set VITE_USE_MOCK=true, or wire POST /api/v1/runs/${runId}/${decision}`
    )
  }
  await new Promise((resolve) => setTimeout(resolve, 220))
  if (decision === "approve") {
    approveSeedRun(runId)
  } else {
    cancelSeedRun(runId)
  }
  return { runId, decision }
}

/** Approving releases the run back to the swarm: it stops waiting on a human. */
export function useApproveRun() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (runId: string) => postDecision(runId, "approve"),
    onMutate: async (runId) => {
      await client.cancelQueries({ queryKey: runsQueryKey })
      const previous = client.getQueryData<RunSummary[]>(runsQueryKey)

      client.setQueryData<RunSummary[]>(runsQueryKey, (runs) =>
        (runs ?? []).map((run) =>
          run.id === runId
            ? {
                ...run,
                status: "running",
                workItems: run.workItems.map((entry) =>
                  entry.id === run.current
                    ? { ...entry, status: "running" as const }
                    : entry
                ),
              }
            : run
        )
      )

      return { previous }
    },
    onError: (_error, _runId, context) => {
      if (context?.previous) {
        client.setQueryData(runsQueryKey, context.previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: runsQueryKey })
    },
  })
}

/** Cancelling tears the container down: the run leaves the swarm entirely. */
export function useCancelRun() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (runId: string) => postDecision(runId, "cancel"),
    onMutate: async (runId) => {
      await client.cancelQueries({ queryKey: runsQueryKey })
      const previous = client.getQueryData<RunSummary[]>(runsQueryKey)

      client.setQueryData<RunSummary[]>(runsQueryKey, (runs) =>
        (runs ?? []).filter((run) => run.id !== runId)
      )

      return { previous }
    },
    onError: (_error, _runId, context) => {
      if (context?.previous) {
        client.setQueryData(runsQueryKey, context.previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: runsQueryKey })
    },
  })
}
