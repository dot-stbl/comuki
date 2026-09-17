import { useQuery } from "@tanstack/react-query"

import type { VerifySnapshot } from "@/domains/verify/model/types"
import { readSeedVerify } from "@/shared/api/mock/verify.store"
import { env } from "@/shared/config/env"

export const verifyQueryKey = ["verify"] as const

/**
 * What the gate says when it is asked for outside mock mode.
 *
 * The host serves no verify endpoints yet, so this is a real and permanent
 * answer in any real deployment — and the sentence the screen shows is read by
 * an operator, not by whoever will one day write those endpoints. What stood
 * here said "set VITE_USE_MOCK=true", which is a note to a developer: an
 * instruction the reader cannot follow, about a build flag they cannot see,
 * printed where the gate's own state belongs. This says what is missing and
 * what it means for them instead.
 *
 * Exported because it is the product's sentence rather than this function's:
 * anything that wants to recognise the unavailable gate — a branch that shows
 * it as a notice rather than an alarm, a test — compares against this and not
 * against a string it re-types.
 */
export const VERIFY_UNAVAILABLE =
  "The verification gate is not connected to this deployment. No checks are read and no run is held by it here — the gate returns when the host serves it."

/**
 * The gate, read from the mutable store rather than from the seed — otherwise
 * the refetch that follows the toggle restores the constant and the switch
 * flips back about two hundred milliseconds later. See
 * `shared/api/mock/verify.store.ts`.
 */
async function getVerify(): Promise<VerifySnapshot> {
  if (!env.useMock) {
    throw new Error(VERIFY_UNAVAILABLE)
  }
  return readSeedVerify()
}

export function useVerifyQuery() {
  return useQuery({
    queryKey: verifyQueryKey,
    queryFn: getVerify,
    /* Nothing to retry. Outside mock mode this fails the same way every time,
       before any request is made, so the default three attempts only delay the
       sentence the operator is owed by a couple of seconds. */
    retry: false,
  })
}
