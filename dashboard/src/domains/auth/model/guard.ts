import { redirect } from "@tanstack/react-router"

import { queryClient } from "@/app/query-client"
import { meQueryOptions } from "@/domains/identity/api/queries"
import { env } from "@/shared/config/env"
import { getMockAuth } from "@/shared/api/mock/auth.store"

export const LOGIN_PATH = "/login"

/** The two fields of a `ParsedLocation` this check actually reads. */
export interface GuardedLocation {
  pathname: string
  /** Path, search and hash together — what the operator was actually asking for. */
  href: string
}

function isLogin(pathname: string): boolean {
  return pathname.replace(/\/+$/, "") === LOGIN_PATH
}

/**
 * The one place the application asks whether anybody is here.
 *
 * It hangs off the root route's `beforeLoad`, so it runs before any screen's
 * loader, component or query — once, for every route there is and every route
 * there will be. Scattered per-screen checks were the alternative and they fail
 * the same way every time: the screen added last is the one that forgot, and
 * the check that runs inside a component has already rendered half a shell to
 * somebody the product cannot name.
 *
 * `/login` is the one exception, and it has to be: the screen whose job is to
 * get you a session cannot require one.
 *
 * This is a client check and therefore a courtesy, not a boundary — the same
 * rule `RequirePermission` states. The API answers for the data.
 *
 * Which session it asks depends on the mode:
 *
 * - **Mock mode** reads the seeded store — storybook and tests rely on it
 *   booting signed in, and there is no host to ask.
 * - **Real mode** (`VITE_USE_MOCK=false`) resolves the *real* session:
 *   `ensureQueryData` on the `me` query (same key, same fetch, same cache
 *   the provider tree reads — `meQueryOptions` is the one source). A cold
 *   visit used to sail past here on the seeded store's say-so and land on a
 *   dashboard of 401s; now the guard waits for `/me`, and a refusal is the
 *   redirect. `ensureQueryData` returns cached-though-stale data and
 *   refetches behind it, so navigation stays instant after the first
 *   resolve — and a refetch that 401s mid-flight is the query-cache
 *   watcher's job, not the guard's.
 */
export async function guardSession(location: GuardedLocation): Promise<void> {
  if (isLogin(location.pathname)) {
    return
  }

  if (!env.useMock) {
    try {
      await queryClient.ensureQueryData(meQueryOptions())
    } catch {
      throw redirect({
        to: LOGIN_PATH,
        search: {
          // A cookie that will not validate reads as expired — the honest
          // sentence for "your session is not there any more", whether it
          // timed out or was never issued.
          reason: "expired",
          // The board is the default landing anyway, so `/` would be noise
          // in the address bar and a redirect that changes nothing.
          ...(location.href === "/" ? {} : { redirect: location.href }),
        },
        // Replace, not push: `back` from the sign-in screen must not return
        // to the screen that just refused them and bounce straight here
        // again.
        replace: true,
      })
    }
    return
  }

  const { user, endedBy } = getMockAuth()
  if (user) {
    return
  }

  throw redirect({
    to: LOGIN_PATH,
    search: {
      // The reason the last session ended is the difference between "you were
      // thrown out" and "you left" — carried through so the landing can say
      // which one happened instead of guessing.
      ...(endedBy ? { reason: endedBy } : {}),
      // The board is the default landing anyway, so `/` would be noise in the
      // address bar and a redirect that changes nothing.
      ...(location.href === "/" ? {} : { redirect: location.href }),
    },
    // Replace, not push: `back` from the sign-in screen must not return to the
    // screen that just refused them and bounce straight here again.
    replace: true,
  })
}
