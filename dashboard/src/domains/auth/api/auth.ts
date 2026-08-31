import { useSyncExternalStore } from "react"

import {
  getMockAuth,
  subscribeMockAuth,
  type MockAuthState,
} from "@/shared/api/mock/auth.store"

/**
 * The signed-in shift, as React state.
 *
 * The mock store is a module, not a query, because a session is not a cache: it
 * has exactly one value at a time, every consumer must see the same one on the
 * same tick, and there is no revalidation story that makes sense for it. So it
 * is subscribed to directly. The store hands back one object per change, which
 * is what `useSyncExternalStore` needs to avoid a render loop.
 */
export function useAuthState(): MockAuthState {
  return useSyncExternalStore(subscribeMockAuth, getMockAuth, getMockAuth)
}
