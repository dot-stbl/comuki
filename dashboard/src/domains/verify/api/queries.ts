import { useQuery } from "@tanstack/react-query"

import type { VerifySnapshot } from "@/domains/verify/model/types"
import { readSeedVerify } from "@/shared/api/mock/verify.store"
import { env } from "@/shared/config/env"

export const verifyQueryKey = ["verify"] as const

/**
 * The gate, read from the mutable store rather than from the seed — otherwise
 * the refetch that follows the toggle restores the constant and the switch
 * flips back about two hundred milliseconds later. See
 * `shared/api/mock/verify.store.ts`.
 */
async function getVerify(): Promise<VerifySnapshot> {
  if (!env.useMock) {
    throw new Error("verify API not implemented — set VITE_USE_MOCK=true")
  }
  return readSeedVerify()
}

export function useVerifyQuery() {
  return useQuery({
    queryKey: verifyQueryKey,
    queryFn: getVerify,
  })
}
