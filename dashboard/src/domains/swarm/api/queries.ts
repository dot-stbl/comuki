import { useQuery } from "@tanstack/react-query"

import type { SwarmCounts } from "@/domains/swarm/model/types"
import { SWARM_SEED } from "@/shared/api/mock"
import { env } from "@/shared/config/env"

export const swarmQueryKey = ["swarm"] as const

async function getSwarm(): Promise<SwarmCounts> {
  if (!env.useMock) {
    throw new Error("swarm API not implemented — set VITE_USE_MOCK=true")
  }
  return { ...SWARM_SEED }
}

export function useSwarmQuery() {
  return useQuery({
    queryKey: swarmQueryKey,
    queryFn: getSwarm,
  })
}
