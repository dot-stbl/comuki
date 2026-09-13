import { useQuery } from "@tanstack/react-query"

import { computeSnapshotWireToSnapshot } from "@/domains/compute/api/mappers"
import type { ComputeSnapshot } from "@/domains/compute/model/types"
import { getApiV1Compute } from "@/shared/api/_generated/clients/getApiV1Compute"
import { readSeedCompute } from "@/shared/api/mock/compute.store"
import { livePolling } from "@/shared/api/polling"
import { env } from "@/shared/config/env"

export const computeQueryKey = ["compute"] as const

/**
 * The registry — the seed store in mock mode, the host's read-only snapshot
 * (`GET /api/v1/compute`) in real mode.
 *
 * The seed shape and the domain shape are the same shape in mock mode, on
 * purpose: the mock is the screen's own description of a full registry. The
 * wire is thinner (no capacity API, no per-label fleet counts), and the
 * mapper degrades each gap onto the nulls the model carries rather than
 * fabricating readings — the day the host composes a compute engine, those
 * fields fill and the screen does not move.
 *
 * Polled on the slowest cadence the platform's surfaces use: the snapshot
 * moves when configuration or queue pressure moves, not when a container
 * breathes.
 */
const COMPUTE_POLL_INTERVAL_MS = 30_000

async function getCompute(): Promise<ComputeSnapshot> {
  if (env.useMock) {
    return readSeedCompute()
  }
  const wire = await getApiV1Compute()
  return computeSnapshotWireToSnapshot(wire)
}

export function useComputeQuery() {
  return useQuery({
    queryKey: computeQueryKey,
    queryFn: getCompute,
    refetchInterval: livePolling(COMPUTE_POLL_INTERVAL_MS),
  })
}
