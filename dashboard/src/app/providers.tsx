import type { ReactNode } from "react"
import { QueryClientProvider } from "@tanstack/react-query"
import { Toaster } from "sonner"

import { ThemeProvider } from "@/app/theme-provider"
import { queryClient, wireUnauthorizedRedirect } from "@/app/query-client"
import { RealtimeProvider } from "@/app/realtime-provider"
import { router } from "@/app/router"
import { useAuthState } from "@/domains/auth"
import { PROJECTS_SEED } from "@/shared/api/mock"
import { SIGNED_OUT_USER } from "@/shared/api/mock/auth.store"
import { SessionProvider } from "@/shared/session"

/**
 * The mid-session 401 exit: any query that comes back 401 in real mode ends
 * the visit here, not on the screen that happened to be mounted.
 *
 * The session is already dead — no logout call (there is no cookie left to
 * revoke and the API would 401 that too). Clear the cache so nothing holds
 * a half-signed-in snapshot, then hand the operator to the sign-in screen
 * with the path they were on, so signing in puts them back. Already on
 * `/login` the watcher stands down: the screen's own `me` probe 401s there
 * by design, and redirecting would be a loop.
 */
wireUnauthorizedRedirect(() => {
  const { pathname, href } = router.state.location
  if (pathname === "/login") {
    return
  }

  queryClient.clear()
  void router.navigate({
    to: "/login",
    search: {
      reason: "expired",
      // The board is the default landing anyway — see the guard's take.
      ...(href === "/" ? {} : { redirect: href }),
    },
    replace: true,
  })
})

export interface AppProvidersProps {
  children: ReactNode
}

/**
 * Boots the auth-aware session inside the QueryClientProvider.
 *
 * `useAuthState` is a `useQuery`, so it needs a client. The old shape called
 * it from `AppProviders` itself, which renders *above* the QueryClientProvider
 * and crashes the first render with "No QueryClient set". The boot moves the
 * hook below the provider; the SessionProvider sits one level deeper so the
 * `user` it forwards is the resolved one, not `undefined`.
 *
 * Until the auth query resolves, children see `SIGNED_OUT_USER` — the screen
 * guard refuses to render anything that needs an identity, so a signed-out
 * boot is a no-op rather than a wrong-tenant leak.
 */
function AuthBoot({ children }: { children: ReactNode }) {
  const { user } = useAuthState()

  return (
    <SessionProvider user={user ?? SIGNED_OUT_USER} projects={PROJECTS_SEED}>
      {/* The socket half of live: starts with a resolved real-mode session,
          stops with a dead one, and turns hub events into invalidation. It
          sits inside the query client (it invalidates) and inside the
          session boot (it reads the same `useAuthState` the boot resolves),
          so socket and REST session share one lifecycle. */}
      <RealtimeProvider>{children}</RealtimeProvider>
    </SessionProvider>
  )
}

export function AppProviders({ children }: AppProvidersProps) {
  return (
    <ThemeProvider defaultTheme="dark" storageKey="comuki-ui-theme">
      {/* The shift, from the mock session store rather than from a constant —
          signing in and signing out have to change what the shell knows, or
          `Sign out` is a navigation with nothing behind it.

          Signed out, the shell is handed a person with no roles rather than the
          seeded engineer: the guard means no screen should reach this state,
          and if one ever does it must show a closed view, not somebody else's
          name and grants.

          It sits above the query client because the project a request is
          scoped to is a parameter of nearly every one of them the day those
          requests are real. */}
      <QueryClientProvider client={queryClient}>
        <AuthBoot>
          {children}
          {/* The proof a write landed — `toast()` is called from a dozen
              screens, but the only Toaster this app ever mounted lived inside
              the shadcn showcase and died with it. It lives at the root now,
              where every screen's toasts render, not just the showcase's.

              Top-centre, because every other corner is taken: bottom-right is
              the console dock's trigger, bottom anything is the sheet itself
              when it is open, and the top corners are the bar's controls.
              Styled from the tokens — a toast is a raised surface carrying a
              sentence, and it should look like one of ours. */}
          <Toaster
            position="top-center"
            toastOptions={{
              style: {
                background: "var(--surface-raised)",
                border: "var(--hairline) solid var(--rule-strong)",
                color: "var(--text)",
                fontFamily: "var(--font-mono)",
                fontSize: "var(--t-sm)",
                borderRadius: "var(--r-md)",
                boxShadow: "var(--shadow-modal)",
              },
            }}
          />
        </AuthBoot>
      </QueryClientProvider>
    </ThemeProvider>
  )
}
