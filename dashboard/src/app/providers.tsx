import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { Toaster } from "sonner"

import { ThemeProvider } from "@/app/theme-provider"
import { useAuthState } from "@/domains/auth"
import { PROJECTS_SEED } from "@/shared/api/mock"
import { SIGNED_OUT_USER } from "@/shared/api/mock/auth.store"
import { SessionProvider } from "@/shared/session"

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

export interface AppProvidersProps {
  children: ReactNode
}

export function AppProviders({ children }: AppProvidersProps) {
  const { user } = useAuthState()

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
      <SessionProvider user={user ?? SIGNED_OUT_USER} projects={PROJECTS_SEED}>
        <QueryClientProvider client={queryClient}>
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
        </QueryClientProvider>
      </SessionProvider>
    </ThemeProvider>
  )
}
