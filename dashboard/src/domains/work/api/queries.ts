import { useQuery } from "@tanstack/react-query"

import { mapWireToWorkTaskSummary } from "@/domains/work/api/mappers"
import type { WorkTaskSummary } from "@/domains/work/model/types"
import { workTasksGet } from "@/shared/api/_generated/clients/workTasksGet"

/**
 * WorkTask queries — the per-task detail via
 * <c>GET /api/v1/work/tasks/{taskId}</c>) and the run-view shape
 * are the only reads today; the kubb client for the detail is
 * statically imported (no <c>await import</c> on the hot read path).
 * Mock mode is wired through the kubb client's mock-first
 * behaviour — the production path uses the kubb-generated
 * <c>workTasksGet</c> / <c>workTasksRunView</c> clients without a
 * code-side branch.
 */

export const workTaskQueryKey = (taskId: string) =>
  ["workTask", taskId] as const

async function fetchWorkTask(taskId: string): Promise<WorkTaskSummary> {
  const wire = await workTasksGet(taskId)
  return mapWireToWorkTaskSummary(wire)
}

export function useWorkTaskQuery(taskId: string) {
  return useQuery({
    queryKey: workTaskQueryKey(taskId),
    queryFn: () => fetchWorkTask(taskId),
    enabled: taskId.length > 0,
  })
}
