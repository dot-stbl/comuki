import { redirect } from "@tanstack/react-router"

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
 */
export function guardSession(location: GuardedLocation): void {
  if (isLogin(location.pathname)) {
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
