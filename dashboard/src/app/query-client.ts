import { QueryCache, QueryClient } from "@tanstack/react-query"

import { env } from "@/shared/config/env"

/**
 * The app's one query client, shared by the provider tree and the session
 * guard (`guardSession` runs in `beforeLoad`, above React, so it reaches the
 * cache through this module rather than a hook).
 *
 * The cache's `onError` is the mid-session 401 watcher the kubb transport
 * deliberately left to someone else ("belongs to a route guard watching
 * /me" — `kubb-client.ts`). When any query comes back 401 in real mode the
 * session is dead: we do NOT call logout (there is nothing left to revoke),
 * we clear the cache and hand the operator to the sign-in screen.
 *
 * Navigation itself is injected via `wireUnauthorizedRedirect` by
 * `AppProviders` — the one module that sees both this client and the router.
 * A static import of the router here would close the cycle
 * client → router → route tree → guard → client, and the route tree
 * evaluates its route imports eagerly, so the cycle would be a crash, not a
 * warning.
 */

/**
 * The registered 401 handler. `null` until `AppProviders` wires it — a
 * query erroring before then (nothing runs before the provider mounts) is
 * handled by the guard instead.
 */
let unauthorizedRedirect: (() => void) | null = null

/** Wired once by `AppProviders`, which owns the router instance. */
export function wireUnauthorizedRedirect(handler: () => void): void {
  unauthorizedRedirect = handler
}

function onQueryError(error: unknown): void {
  if (env.useMock) {
    return
  }

  const status = (error as { status?: number } | null)?.status
  if (status !== 401) {
    return
  }

  unauthorizedRedirect?.()
}

export const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError: onQueryError }),
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})
