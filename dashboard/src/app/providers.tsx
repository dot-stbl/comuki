import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"

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
        </QueryClientProvider>
      </SessionProvider>
    </ThemeProvider>
  )
}
