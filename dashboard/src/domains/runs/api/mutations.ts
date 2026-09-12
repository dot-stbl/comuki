import { useMutation, useQueryClient } from "@tanstack/react-query"

import { runQueryKey, runsQueryKey } from "@/domains/runs/api/queries"
import type { RunSummary } from "@/domains/runs/model/types"
import { postApiV1RunsRunidApprove } from "@/shared/api/_generated/clients/postApiV1RunsRunidApprove"
import { postApiV1RunsRunidCancel } from "@/shared/api/_generated/clients/postApiV1RunsRunidCancel"
import { approveSeedRun, cancelSeedRun } from "@/shared/api/mock"
import { env } from "@/shared/config/env"

/**
 * Run decisions — the two things the duty screen exists to let a human do.
 *
 * Real mode (`VITE_USE_MOCK=false`) calls the kubb-generated clients for
 * `POST /api/v1/runs/{runId}/approve` and `…/{runId}/cancel` (the host's
 * `RunsController` endpoints); cancel carries the request body the wire
 * declares, with `reason` unset because the dashboard asks for no
 * sentence today. Mock mode writes to the shared seed store, which the
 * runs query reads; that round-trip is what keeps the UI honest in
 * storybook and dev:mock.
 *
 * Both hooks keep their optimistic transition (below) and settle by
 * invalidating the runs list **and** the run's own detail key, so the
 * board and an open detail page agree with the host after the decision.
 */

async function postDecision(runId: string, decision: "approve" | "cancel") {
  if (!env.useMock) {
    if (decision === "approve") {
      await postApiV1RunsRunidApprove(runId)
    } else {
      await postApiV1RunsRunidCancel(runId, { reason: null })
    }
    return { runId, decision }
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
    onSettled: async (_data, _error, runId) => {
      await client.invalidateQueries({ queryKey: runsQueryKey })
      await client.invalidateQueries({ queryKey: runQueryKey(runId) })
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
    onSettled: async (_data, _error, runId) => {
      await client.invalidateQueries({ queryKey: runsQueryKey })
      await client.invalidateQueries({ queryKey: runQueryKey(runId) })
    },
  })
}
