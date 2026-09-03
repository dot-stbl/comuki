import { useMemo } from "react"
import { useSyncExternalStore } from "react"

import { useCurrentUserQuery } from "@/domains/identity/api/queries"
import { env } from "@/shared/config/env"
import {
  getMockAuth,
  subscribeMockAuth,
  type MockAuthState,
} from "@/shared/api/mock/auth.store"

/**
 * The signed-in shift, as React state.
 *
 * Two surfaces, one signature:
 *
 * - **Mock mode** (`env.useMock === true`) — the hand-written store. The
 *   store hands back one object per change, which is what
 *   `useSyncExternalStore` needs to avoid a render loop. Tests and stories
 *   keep using `setMockOidcProvider` / `signInMock` directly, unchanged.
 * - **Real mode** (`env.useMock === false`) — the host's `/api/v1/auth/me`
 *   via `useCurrentUserQuery`. Mutations invalidate `meQueryKey`, so the
 *   hook re-renders automatically when the session ends or starts. The
 *   `oidc` field comes from `VITE_OIDC_PROVIDER` — the only fact the SPA
 *   has about whether an identity provider is configured, until a future
 *   `GET /api/v1/auth/oidc/providers` lands on the host.
 *
 * `endedBy` is `null` in real mode: the cookie carries no record of why
 * the last session ended, and the only consumer that needed it
 * (`guardSession`) still reads the mock store directly — a separate
 * concern from this slice.
 */
export function useAuthState(): MockAuthState {
  const mockState = useSyncExternalStore(
    subscribeMockAuth,
    getMockAuth,
    getMockAuth,
  )

  const meQuery = useCurrentUserQuery({ enabled: !env.useMock })

  return useMemo<MockAuthState>(() => {
    if (env.useMock) {
      return mockState
    }

    return {
      user: meQuery.data ?? null,
      endedBy: null,
      oidc: oidcProviderFromEnv(),
    }
  }, [mockState, meQuery.data])
}

function oidcProviderFromEnv(): MockAuthState["oidc"] {
  if (!env.oidcProvider) {
    return null
  }

  return {
    id: env.oidcProvider,
    label: env.oidcProvider,
  }
}